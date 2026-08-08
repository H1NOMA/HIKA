using System.Diagnostics;
using Hika.Audio;
using Hika.Catalog;
using Hika.Config;
using Hika.Diagnostics;
using Hika.Nlu;
using Hika.Overlay;
using Hika.Skills;
using Hika.Stt;
using Hika.Vad;
using Hika.Wake;

namespace Hika;

public enum HostState { Starting, Idle, Sensing, Armed, Working, Failed }

/// <summary>
/// Связывает всё вместе: микрофон, детектор речи, распознавание, разбор команды
/// и свечение.
///
/// Главная тонкость здесь — момент, когда зажигать кайму. Понять, что прозвучало
/// имя, можно только после распознавания, а распознавание идёт по готовой фразе.
/// Если ждать её конца, свечение вспыхнет уже после того, как человек договорил,
/// и вся затея потеряет смысл.
///
/// Поэтому реакция трёхступенчатая:
///   услышали голос              -> едва заметное свечение, ещё не зная, к нам ли;
///   в первом куске нашлось имя  -> кайма разгорается и начинает жить в такт речи;
///   фраза закончилась           -> выполняем команду и коротко подсвечиваем итог.
///
/// Если обращались не к нам, свечение просто гаснет — человек в худшем случае
/// заметит слабый отблеск по краям.
/// </summary>
public sealed class AppHost : IDisposable
{
    private readonly ConfigStore _configStore;
    private readonly MicrophoneCapture _microphone = new();
    private readonly LevelMeter _levelMeter = new();
    private readonly GlowOverlay _overlay = new();
    private readonly AppCatalog _catalog = new();
    private readonly WhisperRecognizer _recognizer = new();

    private IVoiceActivityDetector? _vad;
    private UtteranceSegmenter? _segmenter;
    private WakeWordMatcher? _wakeMatcher;
    private SkillRouter? _router;

    private readonly AutoResetEvent _workSignal = new(false);
    private Thread? _worker;
    private CancellationTokenSource _shutdown = new();

    private readonly object _queueLock = new();
    private float[]? _pendingProbe;
    private float[]? _pendingUtterance;

    private DateTime _armedUntil = DateTime.MinValue;

    // Полное имя намеренно: в проекте WinForms короткое «Timer» неоднозначно,
    // а нам нужен именно фоновый таймер, а не оконный.
    private System.Threading.Timer? _reindexTimer;

    // Не volatile: поле меняется через Interlocked, а передача volatile-поля
    // по ссылке даёт предупреждение и теряет ту самую гарантию, ради которой
    // модификатор и ставился.
    private int _state = (int)HostState.Starting;

    public HostState State => (HostState)Volatile.Read(ref _state);
    public bool Muted => _microphone.Muted;
    public string MicrophoneName => _microphone.ActiveDeviceName;
    public string RecognizerDescription => _recognizer.Description;
    public string VadDescription => _vad?.Name ?? "не запущен";
    public int CatalogSize => _catalog.Count;

    /// <summary>Текущая громкость голоса, 0..1 — для полоски уровня в настройках.</summary>
    public double CurrentLevel => _microphone.Muted ? 0 : _levelMeter.Normalized;

    /// <summary>Состояние изменилось — для значка в трее.</summary>
    public event Action<HostState>? StateChanged;

    /// <summary>Что-то пошло не так на старте и требует внимания человека.</summary>
    public event Action<string>? StartupProblem;

    public AppHost(ConfigStore configStore)
    {
        _configStore = configStore;
    }

    public async Task<bool> StartAsync()
    {
        var config = _configStore.Current;

        Log.Info("=== HIKA запускается ===", "host");

        _catalog.Load(config);
        _router = new SkillRouter(_catalog);
        _wakeMatcher = new WakeWordMatcher(config.Wake);

        if (config.Overlay.Enabled) _overlay.Start(config.Overlay, config.Persona);

        // Детектор речи: сначала пробуем нейросетевой, при неудаче остаёмся
        // на энергетическом. Ждать скачивания модели, чтобы просто запуститься, незачем.
        _vad = new EnergyVad(config.Audio.EnergyThreshold);
        _segmenter = new UtteranceSegmenter(_vad, config.Audio, config.Speech);
        HookSegmenter();

        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "hika-worker",
        };
        _worker.Start();

        if (!_microphone.Start(config.Audio.Device, config.Audio.Gain))
        {
            StartupProblem?.Invoke("Микрофон не найден. Проверьте, что он подключён и разрешён в параметрах приватности Windows.");
            SetState(HostState.Failed);
            return false;
        }

        _microphone.FrameReady += OnAudioFrame;
        _microphone.Muted = config.Behavior.StartMuted;

        // Всё тяжёлое — в фоне: программа должна оказаться в трее сразу,
        // а не через минуту скачивания моделей.
        _ = Task.Run(() => LoadModelsAsync(config));

        if (config.Behavior.IndexInstalledApps)
        {
            _ = Task.Run(() =>
            {
                RefreshInstalledApps();

                var period = TimeSpan.FromMinutes(config.Behavior.ReindexMinutes);
                _reindexTimer = new System.Threading.Timer(_ => RefreshInstalledApps(), null, period, period);
            });
        }

        SetState(HostState.Idle);
        await Task.CompletedTask;
        return true;
    }

    private async Task LoadModelsAsync(HikaConfig config)
    {
        // Детектор речи — первым: он маленький, а пользы от него сразу много.
        try
        {
            var vadModel = await SileroModelProvider.EnsureAsync(
                WhisperModelProvider.ResolveDirectory(config.Speech), _shutdown.Token).ConfigureAwait(false);

            if (vadModel is not null)
            {
                // Оборачиваем в наблюдателя: если нейросетевой детектор откажет
                // молча, программа переключится сама, а не оглохнет навсегда.
                var resilient = new ResilientVad(new SileroVad(vadModel), config.Audio.EnergyThreshold);
                resilient.FellBack += message => StartupProblem?.Invoke(message);

                var previous = _vad;

                _vad = resilient;
                _segmenter = new UtteranceSegmenter(resilient, config.Audio, config.Speech);
                HookSegmenter();

                previous?.Dispose();
                Log.Info("детектор речи переключён на Silero", "host");
            }
        }
        catch (Exception ex)
        {
            Log.Error("не удалось поднять Silero, остаёмся на энергетическом детекторе", ex, "host");
        }

        // Распознавание речи.
        try
        {
            var modelPath = await WhisperModelProvider.EnsureAsync(
                config.Speech,
                (fraction, text) => Log.Info($"модель: {text} ({fraction:P0})", "host"),
                _shutdown.Token).ConfigureAwait(false);

            if (modelPath is null)
            {
                StartupProblem?.Invoke(
                    "Не удалось скачать модель распознавания речи. Проверьте интернет — " +
                    "или положите файл модели вручную, путь указан в файле НАСТРОЙКА.md.");
                return;
            }

            var words = _wakeMatcher?.Words ?? new List<string> { "ави", "хика" };
            if (!await _recognizer.LoadAsync(modelPath, config.Speech, words).ConfigureAwait(false))
            {
                StartupProblem?.Invoke("Модель распознавания не загрузилась. Подробности в журнале.");
                return;
            }

            Log.Info("=== HIKA готова слушать ===", "host");
        }
        catch (Exception ex)
        {
            Log.Error("загрузка распознавания сорвалась", ex, "host");
            StartupProblem?.Invoke("Распознавание речи не запустилось. Подробности в журнале.");
        }
    }

    private void RefreshInstalledApps()
    {
        try { _catalog.SetInstalled(InstalledAppsScanner.Scan()); }
        catch (Exception ex) { Log.Error("обновление списка установленных программ сорвалось", ex, "host"); }
    }

    private void HookSegmenter()
    {
        var segmenter = _segmenter;
        if (segmenter is null) return;

        segmenter.SpeechStarted += OnSpeechStarted;
        segmenter.ProbeReady += OnProbeReady;
        segmenter.UtteranceReady += OnUtteranceReady;
        segmenter.SpeechAborted += OnSpeechAborted;
    }

    // ---- Звук --------------------------------------------------------------

    private void OnAudioFrame(float[] frame, int count)
    {
        var span = frame.AsSpan(0, count);

        _levelMeter.Process(span);
        _overlay.SetLevel(_levelMeter.Normalized);

        _segmenter?.Process(span);

        // Ожидание команды после голого «Ави» не может длиться вечно.
        if (State == HostState.Armed && DateTime.UtcNow > _armedUntil)
        {
            Log.Debug("время ожидания команды вышло", "host");
            Disarm();
        }
    }

    private void OnSpeechStarted()
    {
        if (State == HostState.Working) return;

        if (State == HostState.Armed)
        {
            _overlay.SetState(OverlayState.Listening);
            return;
        }

        SetState(HostState.Sensing);
        _overlay.SetState(OverlayState.Sensing);
    }

    private void OnSpeechAborted()
    {
        if (State == HostState.Sensing)
        {
            SetState(HostState.Idle);
            _overlay.SetState(OverlayState.Hidden);
        }
    }

    private void OnProbeReady(float[] samples, int index)
    {
        // Ранняя проверка нужна только пока мы ещё не знаем, к нам ли обращаются.
        if (State is HostState.Armed or HostState.Working) return;

        lock (_queueLock) _pendingProbe = samples;
        _workSignal.Set();
    }

    private void OnUtteranceReady(float[] samples)
    {
        lock (_queueLock)
        {
            _pendingUtterance = samples;
            // Ранний кусок этой же фразы больше не нужен: сейчас разберём её целиком.
            _pendingProbe = null;
        }

        _workSignal.Set();
    }

    // ---- Разбор ------------------------------------------------------------

    private void WorkerLoop()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                _workSignal.WaitOne(250);

                float[]? utterance;
                float[]? probe;

                lock (_queueLock)
                {
                    utterance = _pendingUtterance;
                    probe = _pendingProbe;
                    _pendingUtterance = null;
                    _pendingProbe = null;
                }

                if (utterance is not null) HandleUtterance(utterance);
                else if (probe is not null) HandleProbe(probe);
            }
            catch (Exception ex)
            {
                Log.Error("сбой в рабочем потоке", ex, "host");
                Thread.Sleep(200);
            }
        }
    }

    private void HandleProbe(float[] samples)
    {
        if (!_recognizer.IsReady || _wakeMatcher is null) return;
        if (State is HostState.Armed or HostState.Working) return;

        var result = _recognizer.TranscribeAsync(samples, _shutdown.Token).GetAwaiter().GetResult();
        if (result.IsEmpty) return;

        var match = _wakeMatcher.Match(result.Text);
        if (!match.Matched) return;

        Log.Info($"имя услышано на ранней проверке: «{result.Text}» ({result.Elapsed.TotalMilliseconds:F0} мс)", "host");

        // Кайма разгорается прямо посреди фразы — человек ещё говорит,
        // а уже видит, что его слушают. Ради этого момента всё и затевалось.
        Arm();
    }

    private void HandleUtterance(float[] samples)
    {
        if (_wakeMatcher is null || _router is null) return;

        if (!_recognizer.IsReady)
        {
            // Самая обидная ситуация из возможных: свечение появляется,
            // человек видит реакцию — и ничего не происходит. Снаружи это
            // выглядит как «слышит, но не понимает», хотя на деле модель
            // ещё качается. Поэтому говорим об этом прямо, а не в журнал.
            Log.Warn("речь услышана, но модель распознавания ещё не готова", "host");
            NotifyRecognizerNotReady();
            ResetToIdle();
            return;
        }

        var wasArmed = State == HostState.Armed && DateTime.UtcNow <= _armedUntil;

        SetState(HostState.Working);
        _overlay.SetState(wasArmed ? OverlayState.Thinking : OverlayState.Sensing);

        var stopwatch = Stopwatch.StartNew();
        var result = _recognizer.TranscribeAsync(samples, _shutdown.Token).GetAwaiter().GetResult();

        if (result.IsEmpty)
        {
            Log.Debug("фраза пустая после очистки", "host");
            ResetToIdle();
            return;
        }

        if (_configStore.Current.Behavior.LogTranscripts)
            Log.Info($"услышано: «{result.Text}» [{result.Language}, {result.Elapsed.TotalMilliseconds:F0} мс]", "host");

        var match = _wakeMatcher.Match(result.Text);

        string command;

        if (match.Matched)
        {
            command = match.Rest;
            Log.Debug($"имя: {match.Word}, уверенность {match.Score:F2}", "host");
        }
        else if (wasArmed)
        {
            // Имя прозвучало в прошлой фразе — эту принимаем как продолжение.
            command = result.Text;
            Log.Debug("имя не прозвучало, но мы ждали команду — принимаю", "host");
        }
        else
        {
            // Обращались не к нам. Молча гаснем.
            Log.Debug("обращение не к нам, пропускаю", "host");
            ResetToIdle();
            return;
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            // Позвали по имени без команды — ждём продолжения.
            Log.Info("позвали по имени, жду команду", "host");
            Arm();
            return;
        }

        _overlay.SetState(OverlayState.Thinking);

        var intent = CommandParser.Parse(command);
        Log.Info($"команда: «{command}» -> {intent}", "host");

        if (!intent.IsActionable)
        {
            Finish(false, stopwatch);
            return;
        }

        var behavior = _configStore.Current.Behavior;

        // Порог узнавания имени намеренно щедрый, и расплачиваться за это
        // браузером, открывшимся с бессмыслицей, человек не должен. Если имя
        // распознано неуверенно, команда обязана однозначно найтись в каталоге.
        if (match.Matched && match.Score < _configStore.Current.Wake.StrictBelowScore && behavior.WebSearchFallback)
        {
            Log.Debug($"имя распознано неуверенно ({match.Score:F2}) — поиск в интернете отключён для этой команды", "host");
            behavior = ShallowCopyWithoutSearchFallback(behavior);
        }

        var outcome = _router.Execute(intent, behavior);
        Log.Info($"итог: {(outcome.Success ? "выполнено" : "не вышло")} — {outcome.Description} (всего {stopwatch.ElapsedMilliseconds} мс)", "host");

        Finish(outcome.Success, stopwatch);
    }

    private static BehaviorConfig ShallowCopyWithoutSearchFallback(BehaviorConfig source) => new()
    {
        ArmedSeconds = source.ArmedSeconds,
        WebSearchFallback = false,
        SearchUrl = source.SearchUrl,
        MatchThreshold = source.MatchThreshold,
        IndexInstalledApps = source.IndexInstalledApps,
        ReindexMinutes = source.ReindexMinutes,
        LogTranscripts = source.LogTranscripts,
        Autostart = source.Autostart,
        StartMuted = source.StartMuted,
        MuteHotkey = source.MuteHotkey,
        LogLevel = source.LogLevel,
    };

    private DateTime _lastNotReadyNotice = DateTime.MinValue;

    private void NotifyRecognizerNotReady()
    {
        // Не чаще раза в полминуты: человек говорит подряд, а всплывающих
        // окон должно быть одно, а не десять.
        if ((DateTime.UtcNow - _lastNotReadyNotice).TotalSeconds < 30) return;
        _lastNotReadyNotice = DateTime.UtcNow;

        StartupProblem?.Invoke(
            "Я вас слышу, но модель распознавания ещё не готова — скорее всего, она " +
            "докачивается. Ход загрузки виден в журнале (значок в трее -> «Журнал работы»).");
    }

    private void Finish(bool success, Stopwatch stopwatch)
    {
        _overlay.SetState(success ? OverlayState.Success : OverlayState.Failed);
        Disarm();
        SetState(HostState.Idle);
    }

    private void Arm()
    {
        _armedUntil = DateTime.UtcNow.AddSeconds(Math.Max(1, _configStore.Current.Behavior.ArmedSeconds));
        SetState(HostState.Armed);
        _overlay.SetState(OverlayState.Listening);
    }

    private void Disarm()
    {
        _armedUntil = DateTime.MinValue;
        if (State == HostState.Armed)
        {
            SetState(HostState.Idle);
            _overlay.SetState(OverlayState.Hidden);
        }
    }

    private void ResetToIdle()
    {
        SetState(HostState.Idle);
        _overlay.SetState(OverlayState.Hidden);
    }

    private void SetState(HostState state)
    {
        var previous = (HostState)Interlocked.Exchange(ref _state, (int)state);
        if (previous == state) return;

        try { StateChanged?.Invoke(state); }
        catch (Exception ex) { Log.Error("обработчик состояния упал", ex, "host"); }
    }

    // ---- Управление --------------------------------------------------------

    public void ToggleMute()
    {
        _microphone.Muted = !_microphone.Muted;

        if (_microphone.Muted)
        {
            _segmenter?.Reset();
            _levelMeter.Reset();
            Disarm();
            _overlay.SetState(OverlayState.Hidden);
        }
    }

    /// <summary>Применяет изменённые настройки. Часть из них требует перезапуска подсистем.</summary>
    public void ApplyConfig(HikaConfig config)
    {
        try
        {
            _microphone.Gain = config.Audio.Gain;
            _segmenter?.Reconfigure(config.Audio, config.Speech);
            _wakeMatcher = new WakeWordMatcher(config.Wake);
            _catalog.Load(config);
            _router = new SkillRouter(_catalog);

            // Свечение пересобираем целиком: цвета, толщина и набор мониторов
            // задаются при создании окон, и подменить их на лету дешевле всего
            // именно так — окна создаются за десятки миллисекунд.
            _overlay.Stop();
            if (config.Overlay.Enabled) _overlay.Start(config.Overlay, config.Persona);

            Log.Info("настройки применены на лету", "host");
        }
        catch (Exception ex)
        {
            Log.Error("применение настроек сорвалось", ex, "host");
        }
    }

    public void Dispose()
    {
        Log.Info("=== HIKA завершается ===", "host");

        try { _shutdown.Cancel(); } catch { }

        _microphone.FrameReady -= OnAudioFrame;
        try { _microphone.Dispose(); } catch { }

        _workSignal.Set();
        try { _worker?.Join(1500); } catch { }

        try { _reindexTimer?.Dispose(); } catch { }
        try { _overlay.Dispose(); } catch { }
        try { _recognizer.Dispose(); } catch { }
        try { _vad?.Dispose(); } catch { }
        try { _shutdown.Dispose(); } catch { }
        try { _workSignal.Dispose(); } catch { }
    }
}
