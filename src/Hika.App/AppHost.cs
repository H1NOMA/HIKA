using System.Diagnostics;
using Hika.Audio;
using Hika.Brain;
using Hika.Catalog;
using Hika.Config;
using Hika.Diagnostics;
using Hika.Learning;
using Hika.Nlu;
using Hika.Overlay;
using Hika.Skills;
using Hika.Speech;
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

    // Отдельная лёгкая модель для ранней проверки имени. Она отвечает
    // на единственный вопрос — прозвучало ли имя, — и тяжёлая модель
    // для этого не нужна. Пока модель была общей, каждая команда стоила
    // трёх тяжёлых распознаваний вместо одного.
    private readonly WhisperRecognizer _probeOwned = new();
    private WhisperRecognizer _probeRecognizer;
    private bool _probeSharesMain;

    private readonly VoiceService _voice = new();
    private readonly ClaudeBrain _brain = new();
    private LearningEngine? _learning;

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

    // Ранняя проверка может идти прямо сейчас, когда фраза уже закончилась.
    // Тогда её надо снимать: иначе готовая команда ждёт, пока договорит
    // проверка, которая уже никому не нужна.
    private CancellationTokenSource? _probeCancellation;

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

    public VoiceService Voice => _voice;
    public ClaudeBrain Brain => _brain;
    public LearningEngine? Learning => _learning;

    /// <summary>Текущая громкость голоса, 0..1 — для полоски уровня в настройках.</summary>
    public double CurrentLevel => _microphone.Muted ? 0 : _levelMeter.Normalized;

    /// <summary>Состояние изменилось — для значка в трее.</summary>
    public event Action<HostState>? StateChanged;

    /// <summary>Что-то пошло не так на старте и требует внимания человека.</summary>
    public event Action<string>? StartupProblem;

    public AppHost(ConfigStore configStore)
    {
        _configStore = configStore;
        _probeRecognizer = _probeOwned;
    }

    public async Task<bool> StartAsync()
    {
        var config = _configStore.Current;

        Log.Info("=== HIKA запускается ===", "host");

        // Наблюдения о человеке поднимаем первыми: от них зависит и словарь
        // распознавания, и поиск по каталогу, и разговор.
        _learning = new LearningEngine(config.Learning);
        _learning.Start();
        _learning.WakeVariantLearned += OnWakeVariantLearned;
        _learning.VocabularyChanged += OnVocabularyChanged;

        _catalog.Prior = _learning;
        _catalog.Load(config);
        _router = new SkillRouter(_catalog);
        _wakeMatcher = new WakeWordMatcher(WithLearnedVariants(config.Wake));

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

        // Голос и разговор — тоже в фоне: перечисление голосов и проверка ключа
        // упираются в систему и в сеть, а до трея дело должно дойти сразу.
        _voice.SpeakingChanged += OnSpeakingChanged;
        _voice.LevelChanged += level => _overlay.SetLevel(level);

        _ = Task.Run(async () =>
        {
            try
            {
                await _voice.StartAsync(config.Voice, config.Speech.Language, _shutdown.Token).ConfigureAwait(false);

                if (config.Voice.Enabled && _voice.SoundsRobotic)
                {
                    StartupProblem?.Invoke(
                        "Голос будет механическим: нейроголоса в Windows не установлены. " +
                        "Ставятся один раз: Параметры → Время и язык → Речь → Управление голосами → " +
                        "Добавить голоса → русский. Либо в настройках HIKA выберите «нейроголоса Microsoft».");
                }
            }
            catch (Exception ex) { Log.Error("озвучка не поднялась", ex, "host"); }

            try
            {
                _brain.ChunkReady += chunk => _voice.Say(chunk);
                _brain.Configure(config.Brain, Personas.ById(config.Persona).Name);
            }
            catch (Exception ex) { Log.Error("разговор не поднялся", ex, "host"); }
        });

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

            // Личный словарь — то самое «обучение». Он подкладывается модели
            // как подсказка перед каждой фразой, и чем дольше человек говорит
            // с программой, тем точнее она слышит именно его слова.
            var vocabulary = _learning?.Vocabulary() ?? Array.Empty<string>();
            if (vocabulary.Count > 0)
                Log.Info($"свой словарь: {string.Join(", ", vocabulary.Take(10))}" +
                         (vocabulary.Count > 10 ? $" и ещё {vocabulary.Count - 10}" : ""), "host");

            if (!await _recognizer.LoadAsync(modelPath, config.Speech, words, vocabulary).ConfigureAwait(false))
            {
                StartupProblem?.Invoke("Модель распознавания не загрузилась. Подробности в журнале.");
                return;
            }

            await LoadProbeModelAsync(config, words).ConfigureAwait(false);

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

    /// <summary>
    /// Поднимает лёгкую модель для ранней проверки имени.
    ///
    /// Если она совпадает с основной, второй экземпляр не создаём: две копии
    /// одной модели в памяти не дают ничего, кроме занятых мегабайт.
    /// </summary>
    private async Task LoadProbeModelAsync(HikaConfig config, IReadOnlyList<string> words)
    {
        var probeName = config.Speech.ProbeModel;

        if (string.IsNullOrWhiteSpace(probeName) ||
            string.Equals(probeName, config.Speech.Model, StringComparison.OrdinalIgnoreCase))
        {
            _probeSharesMain = true;
            _probeRecognizer = _recognizer;
            Log.Info("ранняя проверка имени идёт основной моделью", "host");
            return;
        }

        try
        {
            var path = await WhisperModelProvider.EnsureAsync(
                config.Speech, probeName, null, _shutdown.Token).ConfigureAwait(false);

            if (path is null)
            {
                Log.Warn($"модель «{probeName}» для ранней проверки не скачалась, беру основную", "host");
                _probeSharesMain = true;
                _probeRecognizer = _recognizer;
                return;
            }

            if (await _probeOwned.LoadAsync(path, config.Speech, words).ConfigureAwait(false))
            {
                _probeSharesMain = false;
                _probeRecognizer = _probeOwned;
                Log.Info($"ранняя проверка имени: {Path.GetFileName(path)}", "host");
            }
            else
            {
                _probeSharesMain = true;
                _probeRecognizer = _recognizer;
            }
        }
        catch (Exception ex)
        {
            Log.Error("модель для ранней проверки не поднялась, беру основную", ex, "host");
            _probeSharesMain = true;
            _probeRecognizer = _recognizer;
        }
    }

    // ---- Обучение ----------------------------------------------------------

    /// <summary>
    /// Досыпает в слова пробуждения написания, выученные из собственных
    /// наблюдений. Настройки при этом не трогаются: выученное живёт
    /// в профиле, а не в файле, который человек правит руками.
    /// </summary>
    private WakeConfig WithLearnedVariants(WakeConfig source)
    {
        var learned = _learning?.WakeVariants() ?? Array.Empty<string>();
        if (learned.Count == 0) return source;

        var extra = new List<string>(source.ExtraVariants ?? new List<string>());
        foreach (var variant in learned)
        {
            if (!extra.Contains(variant, StringComparer.OrdinalIgnoreCase)) extra.Add(variant);
        }

        Log.Info($"к имени добавлены выученные написания: {string.Join(", ", learned)}", "host");

        return new WakeConfig
        {
            Words = source.Words,
            ExtraVariants = extra,
            Tolerance = source.Tolerance,
            StrictBelowScore = source.StrictBelowScore,
            AllowAnywhere = source.AllowAnywhere,
            RespondToBoth = source.RespondToBoth,
        };
    }

    private void OnWakeVariantLearned(string variant)
    {
        try
        {
            _wakeMatcher = new WakeWordMatcher(WithLearnedVariants(_configStore.Current.Wake));
            StartupProblem?.Invoke($"Вы зовёте меня «{variant}» — я запомнила это написание и теперь буду откликаться на него сразу.");
        }
        catch (Exception ex)
        {
            Log.Error("новое написание имени не применилось", ex, "host");
        }
    }

    private void OnVocabularyChanged(IReadOnlyList<string> vocabulary)
    {
        // Пересборка встаёт в ту же очередь, что и распознавание, поэтому
        // не сейчас: если человек прямо сейчас говорит, ждать он будет её.
        _ = Task.Run(async () =>
        {
            try
            {
                var words = _wakeMatcher?.Words;
                await _recognizer.UpdateVocabularyAsync(vocabulary, words).ConfigureAwait(false);
            }
            catch (Exception ex) { Log.Error("словарь распознавания не обновился", ex, "host"); }
        });
    }

    // ---- Голос -------------------------------------------------------------

    private void OnSpeakingChanged(bool speaking)
    {
        if (!_configStore.Current.Voice.SuppressMicWhileSpeaking) return;

        _microphone.Suppressed = speaking;

        if (speaking)
        {
            _overlay.SetState(OverlayState.Listening);
            return;
        }

        // Обрывок собственного голоса, успевший попасть во вход, не должен
        // уехать в распознавание — сбрасываем накопленное.
        _segmenter?.Reset();
        _levelMeter.Reset();

        if (State != HostState.Armed) _overlay.SetState(OverlayState.Hidden);
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

        // Пока говорит сама, каймой правит её собственный голос — иначе два
        // источника громкости писали бы в одно поле по очереди, и свет дёргался бы.
        if (!_voice.IsSpeaking) _overlay.SetLevel(_levelMeter.Normalized);

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

        // И ту проверку, что уже идёт, тоже снимаем — она про ту же фразу,
        // а держит очередь перед готовой командой.
        try { _probeCancellation?.Cancel(); } catch { }

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
        var recognizer = _probeRecognizer;
        if (!recognizer.IsReady || _wakeMatcher is null) return;
        if (State is HostState.Armed or HostState.Working) return;

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        _probeCancellation = cancellation;

        RecognitionResult result;
        try
        {
            result = recognizer.TranscribeAsync(samples, cancellation.Token).GetAwaiter().GetResult();
        }
        finally
        {
            _probeCancellation = null;
        }

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

        // Человек заговорил, пока мы отвечаем, — значит, слушать надо его,
        // а не договаривать своё.
        if (_voice.IsSpeaking) _voice.Hush();

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

        WatchRecognitionSpeed(samples.Length, result.Elapsed);

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
            // Обращались не к нам. Молча гаснем — но сначала проверяем,
            // не наше ли это имя, просто расслышанное как что-то другое.
            NoteWakeNearMiss(result.Text);

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

        var journal = new JournalEntry
        {
            Text = result.Text,
            WakeScore = match.Score,
            RecognitionMs = (int)result.Elapsed.TotalMilliseconds,
        };

        var intent = CommandParser.Parse(command);
        Log.Info($"команда: «{command}» -> {intent}", "host");

        // Явный вопрос в каталог даже не заглядывает: «расскажи про Steam» —
        // это просьба рассказать, и потратить на неё поиск по тысяче ярлыков
        // значит подарить человеку лишнюю задержку ради заведомо пустого дела.
        if (Conversation.IsDefinitelyTalk(command) && CanTalk())
        {
            journal.Intent = "разговор";
            journal.Success = Talk(command);
            Record(journal, command);
            FinishTalk(journal.Success, stopwatch);
            return;
        }

        if (!intent.IsActionable)
        {
            if (CanTalk() && Conversation.MightBeTalk(command))
            {
                journal.Intent = "разговор";
                journal.Success = Talk(command);
                Record(journal, command);
                FinishTalk(journal.Success, stopwatch);
                return;
            }

            journal.Intent = "не разобрано";
            Record(journal, command);
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

        journal.Intent = outcome.Success ? outcome.Description : intent.ToString();
        journal.EntryId = outcome.EntryId;
        journal.Success = outcome.Success;

        // Команда не нашлась. Раньше здесь была красная вспышка и молчание;
        // теперь можно хотя бы ответить словами — человек чаще спрашивает,
        // чем командует, и принимать всякий вопрос за неудачный запуск глупо.
        if (!outcome.Success && CanTalk() && _configStore.Current.Brain.AnswerUnknownCommands
            && Conversation.MightBeTalk(command))
        {
            journal.Intent = "разговор";
            journal.Success = Talk(command);
            Record(journal, command);
            FinishTalk(journal.Success, stopwatch);
            return;
        }

        Record(journal, command);

        if (!outcome.Success) SayFailure(outcome.Description);
        else if (_configStore.Current.Voice.SpeakConfirmations) _voice.Say(outcome.Description);

        Finish(outcome.Success, stopwatch);
    }

    // ---- Разговор ----------------------------------------------------------

    private bool CanTalk() => _brain.IsReady && _configStore.Current.Brain.Enabled;

    /// <summary>
    /// Отдаёт фразу в разговор. Ответ произносится по кускам прямо во время
    /// чтения — сюда возвращается только то, вышло ли вообще.
    /// </summary>
    private bool Talk(string question)
    {
        Log.Info($"в разговор: «{question}»", "host");
        _overlay.SetState(OverlayState.Thinking);

        try
        {
            var answer = _brain.AskAsync(question, _learning?.Profile, _shutdown.Token)
                .GetAwaiter().GetResult();

            if (string.IsNullOrWhiteSpace(answer))
            {
                SayFailure("не получилось ответить");
                return false;
            }

            Log.Info($"ответила: «{Shorten(answer)}»", "host");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("разговор сорвался", ex, "host");
            SayFailure("не получилось ответить");
            return false;
        }
    }

    private static string Shorten(string text)
        => text.Length <= 120 ? text : text[..117] + "…";

    private void SayFailure(string what)
    {
        if (!_configStore.Current.Voice.SpeakFailures) return;
        _voice.Say(what);
    }

    /// <summary>
    /// Заканчивает разговорный круг.
    ///
    /// В отличие от команды, здесь мы остаёмся слушать: разговор из одной
    /// реплики разговором не является, и требовать имя перед каждым «а почему?»
    /// значит его убить.
    /// </summary>
    private void FinishTalk(bool success, Stopwatch stopwatch)
    {
        Log.Debug($"разговорный круг за {stopwatch.ElapsedMilliseconds} мс", "host");

        if (!success)
        {
            Finish(false, stopwatch);
            return;
        }

        _armedUntil = DateTime.UtcNow.AddSeconds(
            Math.Max(1, _configStore.Current.Brain.FollowUpSeconds));

        SetState(HostState.Armed);
        _overlay.SetState(OverlayState.Listening);
    }

    // ---- Наблюдения --------------------------------------------------------

    private void Record(JournalEntry entry, string command)
    {
        try { _learning?.Observe(entry, command); }
        catch (Exception ex) { Log.Error("наблюдение не записалось", ex, "host"); }
    }

    /// <summary>
    /// Имя не узнано, но фраза похожа на обращение.
    ///
    /// Это самый ценный и самый упускаемый сигнал. Человек позвал, программа
    /// не откликнулась — и обычно на этом всё: никто не узнаёт, что имя
    /// прозвучало. Здесь мы смотрим, не похоже ли первое слово на имя
    /// и не выглядит ли остаток командой. Если да — запоминаем написание,
    /// и после нескольких повторов программа начнёт откликаться сама.
    /// </summary>
    private void NoteWakeNearMiss(string text)
    {
        if (_learning is null || _wakeMatcher is null) return;

        var tokens = TextNormalizer.Tokenize(text);
        if (tokens.Length < 2) return;

        var similarity = _wakeMatcher.Similarity(tokens[0]);
        if (similarity < 0.3) return;

        // Проверяем, выглядит ли остаток осмысленной командой. Без этого
        // в написания имени уедет каждое похожее слово из случайного разговора.
        var rest = string.Join(' ', tokens[1..]);
        var intent = CommandParser.Parse(rest);

        var looksLikeCommand = intent.ExplicitVerb
            || (intent.Kind != IntentKind.Launch && intent.IsActionable)
            || _catalog.Resolve(rest, 0.85) is not null;

        if (!looksLikeCommand) return;

        Log.Debug($"похоже, звали меня: «{tokens[0]}» (сходство {similarity:F2}), команда «{rest}»", "host");
        _learning.ObserveWakeAttempt(tokens[0], similarity);
    }

    private int _slowRecognitions;
    private bool _slownessReported;

    /// <summary>
    /// Следит, успевает ли распознавание за речью.
    ///
    /// Модель, считающая дольше, чем длится сама фраза, превращает ассистента
    /// в бесполезный: человек не станет ждать по полминуты, каким бы точным
    /// ни был ответ. Сказать об этом надо прямо и с готовым решением —
    /// сам он на модель не подумает.
    /// </summary>
    private void WatchRecognitionSpeed(int sampleCount, TimeSpan elapsed)
    {
        if (_slownessReported) return;

        var audioSeconds = sampleCount / (double)AudioFormat.SampleRate;
        if (audioSeconds < 0.5) return;

        var ratio = elapsed.TotalSeconds / audioSeconds;
        if (ratio < 1.2)
        {
            _slowRecognitions = 0;
            return;
        }

        Log.Warn($"распознавание отстаёт от речи: {elapsed.TotalSeconds:F1} с на {audioSeconds:F1} с звука", "host");

        if (++_slowRecognitions < 2) return;
        _slownessReported = true;

        var model = _configStore.Current.Speech.Model;
        var faster = model.Equals("largev3turbo", StringComparison.OrdinalIgnoreCase) ? "small"
                   : model.Equals("medium", StringComparison.OrdinalIgnoreCase) ? "small"
                   : model.Equals("small", StringComparison.OrdinalIgnoreCase) ? "base"
                   : null;

        var message = faster is null
            ? $"Распознавание не успевает за речью (примерно в {ratio:F1} раза медленнее). " +
              "Помогло бы ускорение на видеокарте — см. сборку hika-win-x64-vulkan."
            : $"Распознавание не успевает за речью (примерно в {ratio:F1} раза медленнее). " +
              $"Смените модель на «{faster}» в настройках — или возьмите сборку с ускорением " +
              "на видеокарте, hika-win-x64-vulkan.";

        StartupProblem?.Invoke(message);
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

    /// <summary>Прогоняет свечение по всем состояниям — проверка без микрофона.</summary>
    public void TestOverlay() => _ = Task.Run(() => _overlay.RunSelfTestAsync(_shutdown.Token));

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
            _wakeMatcher = new WakeWordMatcher(WithLearnedVariants(config.Wake));
            _learning?.Reconfigure(config.Learning);
            _catalog.Load(config);
            _router = new SkillRouter(_catalog);

            // Голос и разговор — в фоне: перечисление голосов лезет в систему,
            // а окно настроек должно закрыться сразу, а не через секунду.
            _ = Task.Run(async () =>
            {
                try
                {
                    await _voice.StartAsync(config.Voice, config.Speech.Language, _shutdown.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) { Log.Error("озвучка не перенастроилась", ex, "host"); }

                try { _brain.Configure(config.Brain, Personas.ById(config.Persona).Name); }
                catch (Exception ex) { Log.Error("разговор не перенастроился", ex, "host"); }
            });

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
        try { _voice.Dispose(); } catch { }
        try { _brain.Dispose(); } catch { }

        // Профиль сбрасывается на диск здесь — до этого он живёт в памяти
        // и пишется отложенно, чтобы не тревожить диск на каждое слово.
        try { _learning?.Dispose(); } catch { }

        try { _overlay.Dispose(); } catch { }
        try { _recognizer.Dispose(); } catch { }
        if (!_probeSharesMain) { try { _probeOwned.Dispose(); } catch { } }
        try { _vad?.Dispose(); } catch { }
        try { _shutdown.Dispose(); } catch { }
        try { _workSignal.Dispose(); } catch { }
    }
}
