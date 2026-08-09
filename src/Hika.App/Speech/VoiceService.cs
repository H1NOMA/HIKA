using System.Collections.Concurrent;
using System.Text;
using Hika.Config;
using Hika.Diagnostics;
using NAudio.Wave;

namespace Hika.Speech;

/// <summary>
/// Голос ассистента: выбор движка, очередь фраз, проигрывание, замолкание.
///
/// Три вещи, ради которых этот класс существует отдельно от синтеза.
///
/// Первая — очередь. Ответ приходит потоком, и произносить его целиком после
/// того, как он дописан, значит подарить человеку лишние секунды тишины.
/// Поэтому фразы уходят в озвучку по мере готовности и проигрываются подряд,
/// пока модель ещё дописывает следующую.
///
/// Вторая — микрофон. Из колонок собственный голос слышен так же хорошо,
/// как чужой, и без заглушения ассистент услышит сам себя, распознает
/// и в худшем случае ответит на собственную реплику. Поэтому на время речи
/// вход глушится, а после — сбрасывается накопленное, чтобы обрывок
/// собственного голоса не уехал в распознавание.
///
/// Третья — умение замолчать. Если человек заговорил, пока ассистент отвечает,
/// перебивать его собственной речью невежливо и просто мешает.
/// </summary>
public sealed class VoiceService : IDisposable
{
    private readonly BlockingCollection<string> _queue = new(new ConcurrentQueue<string>());
    private readonly WindowsSpeaker _windows = new();
    private readonly EdgeSpeaker _edge = new();

    private Thread? _worker;
    private CancellationTokenSource _shutdown = new();
    private CancellationTokenSource? _current;

    private VoiceConfig _config = new();
    private string _language = "ru";
    private ISpeaker? _primary;
    private ISpeaker? _fallback;

    private volatile bool _speaking;
    private volatile bool _ready;

    /// <summary>Начала или закончила говорить. Здесь глушится микрофон.</summary>
    public event Action<bool>? SpeakingChanged;

    /// <summary>Громкость собственного голоса, 0..1 — чтобы кайма жила в такт и её речи тоже.</summary>
    public event Action<double>? LevelChanged;

    public bool IsSpeaking => _speaking;
    public bool IsReady => _ready;

    /// <summary>Что вышло выбрать. Для окна настроек и журнала.</summary>
    public string Description { get; private set; } = "не готов";

    /// <summary>Голос механический, потому что нейроголоса найти не удалось.</summary>
    public bool SoundsRobotic { get; private set; }

    public IReadOnlyList<VoiceInfo> AvailableVoices =>
        _primary?.Voices ?? (IReadOnlyList<VoiceInfo>)Array.Empty<VoiceInfo>();

    public async Task StartAsync(VoiceConfig config, string language, CancellationToken ct = default)
    {
        _config = config;
        _language = language;

        if (!config.Enabled || config.Engine.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            Description = "выключен";
            _ready = false;
            return;
        }

        await ChooseEngineAsync(ct).ConfigureAwait(false);

        if (_worker is null)
        {
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "hika-voice" };
            _worker.Start();
        }
    }

    private async Task ChooseEngineAsync(CancellationToken ct)
    {
        var engine = (_config.Engine ?? "auto").Trim().ToLowerInvariant();

        // Порядок предпочтений — это и есть вся политика выбора.
        //
        // «auto» не лезет в интернет по собственной инициативе: сначала ищет
        // нейроголос, установленный в самой Windows. Он звучит так же хорошо
        // и при этом никуда ничего не отправляет. Голоса из интернета
        // включаются только тогда, когда человек выбрал их сам.
        var neuralOnly = _config.NeuralOnly;

        if (engine == "edge")
        {
            if (await _edge.PrepareAsync(_config.Voice, _language, neuralOnly, ct).ConfigureAwait(false))
            {
                _primary = _edge;

                // Местный голос держим наготове: интернет пропадает, а отвечать надо.
                // Но только если он нейросетевой — механическим запасным вариантом
                // ответ становится хуже, чем неотвеченным.
                if (await _windows.PrepareAsync("", _language, neuralOnly, ct).ConfigureAwait(false))
                    _fallback = _windows;

                Finish(_edge, robotic: false);
                return;
            }

            Log.Warn("нейроголоса Microsoft недоступны, перехожу на голос Windows", "voice");
            engine = "windows";
        }

        if (await _windows.PrepareAsync(_config.Voice, _language, neuralOnly, ct).ConfigureAwait(false))
        {
            _primary = _windows;
            _fallback = null;
            Finish(_windows, robotic: !_windows.UsingNeural);
            return;
        }

        // Нейроголосов в Windows нет. Раньше здесь брался механический —
        // и ровно за это пришлось выслушать, что звук режет ухо. Теперь
        // остаётся интернет, а если и его нет — молчание.
        if (engine != "edge" && await _edge.PrepareAsync(_config.Voice, _language, neuralOnly, ct).ConfigureAwait(false))
        {
            _primary = _edge;
            Finish(_edge, robotic: false);
            NeuralMissing = !_windows.HasNeuralVoice;
            return;
        }

        NeuralMissing = !_windows.HasNeuralVoice && _windows.Voices.Count > 0;

        Description = NeuralMissing
            ? "нейроголоса не установлены — молчу"
            : "голосов в системе не нашлось";

        _ready = false;

        Log.Warn(NeuralMissing
            ? "озвучивать нечем: нейроголосов в системе нет, а механическим говорить не буду"
            : "озвучивать нечем: ни голосов Windows, ни доступа к нейроголосам", "voice");
    }

    /// <summary>
    /// Голоса в системе есть, но все механические.
    ///
    /// Отличается от «голосов нет вовсе» тем, что здесь у человека
    /// есть понятное действие: поставить нейроголоса за пять минут.
    /// </summary>
    public bool NeuralMissing { get; private set; }

    private void Finish(ISpeaker speaker, bool robotic)
    {
        SoundsRobotic = robotic;
        _ready = true;
        Description = speaker.Current is null ? speaker.Name : $"{speaker.Name}: {speaker.Current.Describe()}";
        Log.Info($"озвучка: {Description}", "voice");
    }

    // ---- Что сказать -------------------------------------------------------

    /// <summary>Ставит фразу в очередь. Возврат немедленный.</summary>
    public void Say(string text)
    {
        if (!_ready || string.IsNullOrWhiteSpace(text)) return;

        var cleaned = SpeechText.ForSpeaking(text);
        if (cleaned.Length == 0) return;

        try { _queue.Add(cleaned); }
        catch (Exception ex) { Log.Debug($"фраза не встала в очередь: {ex.Message}", "voice"); }
    }

    /// <summary>
    /// Замолчать немедленно и забыть всё, что не сказано.
    /// Вызывается, когда человек заговорил сам.
    /// </summary>
    public void Hush()
    {
        while (_queue.TryTake(out _)) { }
        try { _current?.Cancel(); } catch { }
    }

    private void WorkerLoop()
    {
        foreach (var text in _queue.GetConsumingEnumerable())
        {
            if (_shutdown.IsCancellationRequested) return;

            try { SpeakOne(text); }
            catch (Exception ex) { Log.Error("озвучка сорвалась", ex, "voice"); }
        }
    }

    private void SpeakOne(string text)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        _current = cancellation;

        try
        {
            var audio = Synthesize(text, cancellation.Token);
            if (audio is null) return;

            using (audio)
            {
                SetSpeaking(true);
                Play(audio, cancellation.Token);
            }
        }
        finally
        {
            _current = null;

            // Тишину объявляем только когда очередь опустела: между двумя
            // фразами одного ответа микрофон включать бессмысленно, зато
            // щелчки заглушения человек услышит.
            if (_queue.Count == 0) SetSpeaking(false);
        }
    }

    private SynthesizedAudio? Synthesize(string text, CancellationToken ct)
    {
        var settings = new VoiceSettings(_config.Rate, _config.Volume);

        var primary = _primary;
        if (primary is not null)
        {
            var result = primary.SynthesizeAsync(text, settings, ct).GetAwaiter().GetResult();
            if (result is not null) return result;
        }

        var fallback = _fallback;
        if (fallback is null || ct.IsCancellationRequested) return null;

        Log.Info("основной голос не справился — говорю запасным", "voice");
        return fallback.SynthesizeAsync(text, settings, ct).GetAwaiter().GetResult();
    }

    private void Play(SynthesizedAudio audio, CancellationToken ct)
    {
        using var reader = audio.Container == AudioContainer.Mp3
            ? (WaveStream)new Mp3FileReader(audio.Data)
            : new WaveFileReader(audio.Data);

        // Отвод на измеритель: кайма должна дышать и под собственный голос,
        // иначе ассистент говорит из тишины, будто это не он.
        var metered = new LevelTap(reader.ToSampleProvider(), level => LevelChanged?.Invoke(level));

        using var output = new WaveOutEvent { DesiredLatency = 120 };
        output.Init(metered);
        output.Play();

        while (output.PlaybackState == PlaybackState.Playing)
        {
            if (ct.IsCancellationRequested)
            {
                try { output.Stop(); } catch { }
                break;
            }
            Thread.Sleep(30);
        }

        LevelChanged?.Invoke(0);
    }

    private void SetSpeaking(bool speaking)
    {
        if (_speaking == speaking) return;
        _speaking = speaking;

        if (!speaking) LevelChanged?.Invoke(0);

        try { SpeakingChanged?.Invoke(speaking); }
        catch (Exception ex) { Log.Error("обработчик состояния озвучки упал", ex, "voice"); }
    }

    /// <summary>Список голосов для окна настроек — из обоих движков.</summary>
    public async Task<IReadOnlyList<VoiceInfo>> ListAllVoicesAsync(CancellationToken ct = default)
    {
        var result = new List<VoiceInfo>();

        try
        {
            // Здесь спрашиваем без ограничения: список показывает всё, что есть
            // в системе, включая механические. Выбирать из них — другой вопрос.
            await _windows.PrepareAsync(_config.Voice, _language, neuralOnly: false, ct).ConfigureAwait(false);
            result.AddRange(_windows.Voices);
        }
        catch { }

        if (_config.Engine.Equals("edge", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await _edge.PrepareAsync(_config.Voice, _language, neuralOnly: true, ct).ConfigureAwait(false);
                result.AddRange(_edge.Voices);
            }
            catch { }
        }

        return result;
    }

    public void Dispose()
    {
        try { _shutdown.Cancel(); } catch { }
        Hush();

        try { _queue.CompleteAdding(); } catch { }
        try { _worker?.Join(1500); } catch { }

        try { _windows.Dispose(); } catch { }
        try { _edge.Dispose(); } catch { }
        try { _queue.Dispose(); } catch { }
        try { _shutdown.Dispose(); } catch { }
    }
}

/// <summary>
/// Пропускает звук насквозь и попутно измеряет громкость.
///
/// Нужно ровно для одного: чтобы свечение по краям экрана жило в такт
/// голосу ассистента так же, как оно живёт в такт голосу человека.
/// </summary>
internal sealed class LevelTap : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly Action<double> _report;
    private double _smoothed;

    public LevelTap(ISampleProvider source, Action<double> report)
    {
        _source = source;
        _report = report;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        if (read <= 0) return read;

        double sum = 0;
        for (int i = 0; i < read; i++)
        {
            var sample = buffer[offset + i];
            sum += sample * sample;
        }

        var rms = Math.Sqrt(sum / read);

        // Сглаживание, иначе кайма дёргается на каждом кадре.
        // Вверх быстро, вниз медленно — так свет ведёт себя естественно.
        var target = Math.Clamp(rms * 3.2, 0, 1);
        _smoothed = target > _smoothed ? target : _smoothed * 0.82 + target * 0.18;

        try { _report(_smoothed); } catch { }
        return read;
    }
}
