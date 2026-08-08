using Hika.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Hika.Audio;

public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault);

/// <summary>
/// Захват с микрофона через WASAPI с приведением к 16 кГц моно.
///
/// Устройство отдаёт что угодно — обычно 48 кГц стерео float, но бывает
/// и 44.1 кГц, и шесть каналов. Вся эта разница гасится здесь, а наружу
/// идёт ровный поток кадров по 32 мс.
///
/// Класс сам переживает пропажу устройства: если микрофон выдернули,
/// поток перезапускается, когда он вернётся.
/// </summary>
public sealed class MicrophoneCapture : IDisposable
{
    private readonly object _lock = new();

    private MMDeviceEnumerator? _enumerator;
    private WasapiCapture? _capture;
    private BufferedWaveProvider? _incoming;
    private ISampleProvider? _chain;
    private Thread? _pump;

    private volatile bool _running;
    private volatile bool _muted;
    private volatile bool _suppressed;
    private volatile bool _restartRequested;

    private float _gain = 1.0f;
    private string _deviceFilter = "";
    private DateTime _lastRestart = DateTime.MinValue;

    /// <summary>
    /// Кадр по 512 отсчётов (32 мс), 16 кГц моно.
    /// Буфер переиспользуется — если данные нужны дольше вызова, копируйте.
    /// </summary>
    public event Action<float[], int>? FrameReady;

    /// <summary>Захват прервался и не восстановился сам.</summary>
    public event Action<string>? Failed;

    public bool IsRunning => _running;
    public string ActiveDeviceName { get; private set; } = "";
    public WaveFormat? SourceFormat { get; private set; }

    public bool Muted
    {
        get => _muted;
        set
        {
            if (_muted == value) return;
            _muted = value;
            Log.Info(value ? "микрофон выключен" : "микрофон включён", "audio");
        }
    }

    /// <summary>
    /// Временное заглушение — на то время, пока ассистент говорит сам.
    ///
    /// Отдельно от <see cref="Muted"/> намеренно: то выключил человек, и трогать
    /// его нельзя. Если бы озвучка пользовалась тем же переключателем, она
    /// включала бы микрофон, который человек только что выключил.
    /// </summary>
    public bool Suppressed
    {
        get => _suppressed;
        set => _suppressed = value;
    }

    public float Gain
    {
        get => _gain;
        set => _gain = Math.Clamp(value, 0.05f, 12f);
    }

    public static IReadOnlyList<AudioDeviceInfo> ListDevices()
    {
        var result = new List<AudioDeviceInfo>();
        try
        {
            using var en = new MMDeviceEnumerator();
            string defaultId = "";
            try { defaultId = en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications).ID; }
            catch { /* устройств может не быть вовсе */ }

            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                result.Add(new AudioDeviceInfo(d.ID, d.FriendlyName, d.ID == defaultId));
        }
        catch (Exception ex)
        {
            Log.Error("не удалось получить список микрофонов", ex, "audio");
        }
        return result;
    }

    /// <param name="deviceFilter">Часть названия устройства. Пусто — устройство по умолчанию.</param>
    public bool Start(string deviceFilter = "", float gain = 1.0f)
    {
        lock (_lock)
        {
            if (_running) Stop();

            _deviceFilter = deviceFilter ?? "";
            Gain = gain;

            try
            {
                _enumerator = new MMDeviceEnumerator();
                var device = ResolveDevice(_enumerator, _deviceFilter);
                if (device is null)
                {
                    Failed?.Invoke("микрофон не найден");
                    Log.Error("активных микрофонов в системе нет", "audio");
                    return false;
                }

                ActiveDeviceName = device.FriendlyName;

                // Общий режим (он же по умолчанию): монопольный захват отобрал бы
                // микрофон у всех остальных программ, чего фоновому ассистенту делать нельзя.
                _capture = new WasapiCapture(device);

                var format = NormalizeFormat(_capture.WaveFormat);
                SourceFormat = format;

                _incoming = new BufferedWaveProvider(format)
                {
                    BufferDuration = TimeSpan.FromSeconds(4),
                    DiscardOnBufferOverflow = true,

                    // Принципиально. По умолчанию NAudio дополняет недостающее
                    // тишиной и всегда возвращает полный запрошенный кусок —
                    // тогда качающий поток крутился бы вхолостую на полной
                    // скорости, выдавая выдуманные кадры тишины быстрее
                    // реального времени. Нам нужно ровно то, что реально пришло
                    // с микрофона, а на пустом буфере — подождать.
                    ReadFully = false,
                };

                _chain = BuildChain(_incoming);

                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capture.StartRecording();

                _running = true;
                _restartRequested = false;

                _pump = new Thread(PumpLoop)
                {
                    IsBackground = true,
                    Name = "hika-audio-pump",
                    Priority = ThreadPriority.AboveNormal,
                };
                _pump.Start();

                Log.Info($"микрофон «{ActiveDeviceName}», вход {format.SampleRate} Гц / {format.Channels} кан. -> 16000 Гц моно", "audio");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("не удалось запустить захват звука", ex, "audio");
                Failed?.Invoke(ex.Message);
                CleanupUnsafe();
                return false;
            }
        }
    }

    private static MMDevice? ResolveDevice(MMDeviceEnumerator en, string filter)
    {
        var active = en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();
        if (active.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            var match = active.FirstOrDefault(d =>
                d.FriendlyName.Contains(filter, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;

            Log.Warn($"микрофон «{filter}» не найден, берём устройство по умолчанию", "audio");
        }

        try { return en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications); }
        catch { return active[0]; }
    }

    /// <summary>
    /// WASAPI обычно отдаёт WaveFormatExtensible, который конвертеры NAudio
    /// не всегда узнают. Приводим к обычному описанию — байты те же.
    /// </summary>
    private static WaveFormat NormalizeFormat(WaveFormat wf)
    {
        if (wf is WaveFormatExtensible ext)
        {
            try { return ext.ToStandardWaveFormat(); }
            catch { /* ниже соберём вручную */ }
        }

        if (wf.Encoding is WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Pcm) return wf;

        return wf.BitsPerSample == 32
            ? WaveFormat.CreateIeeeFloatWaveFormat(wf.SampleRate, wf.Channels)
            : new WaveFormat(wf.SampleRate, wf.BitsPerSample, wf.Channels);
    }

    private static ISampleProvider BuildChain(BufferedWaveProvider source)
    {
        ISampleProvider sp = source.ToSampleProvider();

        if (sp.WaveFormat.Channels > 1)
            sp = new MonoMixSampleProvider(sp);

        if (sp.WaveFormat.SampleRate != AudioFormat.SampleRate)
        {
            // WDL — приличный по качеству пересчёт частоты и при этом полностью
            // управляемый код: никаких дополнительных native-зависимостей.
            sp = new WdlResamplingSampleProvider(sp, AudioFormat.SampleRate);
        }

        return sp;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;
        try { _incoming?.AddSamples(e.Buffer, 0, e.BytesRecorded); }
        catch (Exception ex) { Log.Warn($"кадр с микрофона потерян: {ex.Message}", "audio"); }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (!_running) return;

        if (e.Exception is not null)
            Log.Warn($"захват прервался: {e.Exception.Message}", "audio");

        _restartRequested = true;
    }

    private void PumpLoop()
    {
        var frame = new float[AudioFormat.FrameSamples];
        var idleSpins = 0;

        while (_running)
        {
            try
            {
                if (_restartRequested)
                {
                    TryRestart();
                    continue;
                }

                var chain = _chain;
                if (chain is null) { Thread.Sleep(20); continue; }

                var read = chain.Read(frame, 0, frame.Length);

                if (read <= 0)
                {
                    // Пусто — ждём. Спим коротко, чтобы не добавлять задержки к отклику.
                    if (++idleSpins > 200) { Thread.Sleep(8); idleSpins = 200; }
                    else Thread.Sleep(2);
                    continue;
                }

                idleSpins = 0;

                // Недобор до полного кадра дополняем тишиной: и VAD, и измеритель
                // рассчитывают на постоянный размер окна.
                if (read < frame.Length) Array.Clear(frame, read, frame.Length - read);

                if (_muted || _suppressed)
                {
                    Array.Clear(frame, 0, frame.Length);
                }
                else if (Math.Abs(_gain - 1.0f) > 0.001f)
                {
                    var g = _gain;
                    for (int i = 0; i < frame.Length; i++)
                        frame[i] = Math.Clamp(frame[i] * g, -1f, 1f);
                }

                try { FrameReady?.Invoke(frame, frame.Length); }
                catch (Exception ex) { Log.Error("обработчик аудиокадра упал", ex, "audio"); }
            }
            catch (Exception ex)
            {
                Log.Error("сбой в потоке захвата", ex, "audio");
                _restartRequested = true;
                Thread.Sleep(500);
            }
        }
    }

    /// <summary>Переподключение к устройству — например, после того как микрофон вернули в USB.</summary>
    private void TryRestart()
    {
        // Не чаще раза в три секунды, иначе при отсутствующем устройстве получим бесконечный цикл.
        if ((DateTime.UtcNow - _lastRestart).TotalSeconds < 3) { Thread.Sleep(500); return; }
        _lastRestart = DateTime.UtcNow;

        Log.Info("переподключение к микрофону", "audio");

        lock (_lock)
        {
            try
            {
                if (_capture is not null)
                {
                    _capture.DataAvailable -= OnDataAvailable;
                    _capture.RecordingStopped -= OnRecordingStopped;
                    try { _capture.StopRecording(); } catch { }
                    _capture.Dispose();
                    _capture = null;
                }

                _enumerator?.Dispose();
                _enumerator = new MMDeviceEnumerator();

                var device = ResolveDevice(_enumerator, _deviceFilter);
                if (device is null) { Thread.Sleep(2000); return; }

                ActiveDeviceName = device.FriendlyName;
                _capture = new WasapiCapture(device) { ShareMode = AudioClientShareMode.Shared };

                var format = NormalizeFormat(_capture.WaveFormat);
                SourceFormat = format;

                _incoming = new BufferedWaveProvider(format)
                {
                    BufferDuration = TimeSpan.FromSeconds(4),
                    DiscardOnBufferOverflow = true,

                    // Принципиально. По умолчанию NAudio дополняет недостающее
                    // тишиной и всегда возвращает полный запрошенный кусок —
                    // тогда качающий поток крутился бы вхолостую на полной
                    // скорости, выдавая выдуманные кадры тишины быстрее
                    // реального времени. Нам нужно ровно то, что реально пришло
                    // с микрофона, а на пустом буфере — подождать.
                    ReadFully = false,
                };
                _chain = BuildChain(_incoming);

                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capture.StartRecording();

                _restartRequested = false;
                Log.Info($"микрофон снова на связи: «{ActiveDeviceName}»", "audio");
            }
            catch (Exception ex)
            {
                Log.Warn($"переподключение не удалось: {ex.Message}", "audio");
                Thread.Sleep(2000);
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _running = false;
            CleanupUnsafe();
        }

        try { _pump?.Join(1500); } catch { }
        _pump = null;
    }

    private void CleanupUnsafe()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            try { _capture.StopRecording(); } catch { }
            try { _capture.Dispose(); } catch { }
            _capture = null;
        }

        try { _enumerator?.Dispose(); } catch { }
        _enumerator = null;

        _incoming = null;
        _chain = null;
    }

    public void Dispose() => Stop();
}
