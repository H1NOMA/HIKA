using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Hika.Config;
using Hika.Diagnostics;
using Hika.Interop;
using Microsoft.Win32;

namespace Hika.Overlay;

/// <summary>
/// Свечение по краям экрана.
///
/// Живёт в собственном потоке с собственным циклом сообщений: рисование
/// не должно ни ждать распознавания речи, ни задерживать его. Из остальных
/// частей программы сюда приходят только два значения — состояние и текущая
/// громкость голоса.
///
/// В покое стоит ровно ничего: окна скрыты, обновлений нет.
/// </summary>
public sealed class GlowOverlay : IDisposable
{
    private readonly object _lock = new();

    private Thread? _uiThread;
    private ApplicationContext? _context;
    private Thread? _ticker;

    private readonly List<GlowBandWindow> _bands = new();
    private GlowBandWindow? _anchor;

    private OverlayConfig _config = new();
    private GlowPalette _palette = new(new OverlayConfig());
    private string _personaId = "hika";

    private volatile bool _running;
    private volatile bool _framePending;

    // Меняется через Interlocked, поэтому без volatile — см. пояснение в AppHost.
    private int _state = (int)OverlayState.Hidden;
    private double _level;
    private int _colorGroup = -1;

    private readonly double[] _current = new double[4];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _flashStarted = -1;

    /// <summary>Сколько длится вспышка после выполненной команды.</summary>
    private const double FlashSeconds = 0.85;

    public bool IsRunning => _running;
    public OverlayState State => (OverlayState)Volatile.Read(ref _state);

    public void Start(OverlayConfig config, string personaId = "hika")
    {
        lock (_lock)
        {
            if (_running) return;
            if (!config.Enabled)
            {
                Log.Info("свечение выключено в настройках", "overlay");
                return;
            }

            _config = config;
            _personaId = personaId;
            _palette = new GlowPalette(config, personaId);
            _running = true;

            _uiThread = new Thread(UiThreadBody)
            {
                IsBackground = true,
                Name = "hika-overlay",
            };

            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();

            _ticker = new Thread(TickerBody)
            {
                IsBackground = true,
                Name = "hika-overlay-tick",
            };

            _ticker.Start();
        }
    }

    /// <summary>Меняет состояние. Можно звать из любого потока.</summary>
    public void SetState(OverlayState state)
    {
        var previous = (OverlayState)Interlocked.Exchange(ref _state, (int)state);
        if (previous == state) return;

        if (state is OverlayState.Success or OverlayState.Failed)
            Volatile.Write(ref _flashStarted, _clock.Elapsed.TotalSeconds);

        Log.Trace($"свечение: {previous} -> {state}", "overlay");
    }

    /// <summary>Текущая громкость голоса, 0..1. Приходит с потока звука тридцать раз в секунду.</summary>
    public void SetLevel(double level) => Volatile.Write(ref _level, Math.Clamp(level, 0, 1));

    /// <summary>
    /// Прогоняет свечение по всем состояниям, не трогая микрофон.
    ///
    /// Отвечает на вопрос, который иначе не отделить: «кайма не появляется»
    /// может значить и что сломана отрисовка, и что до неё просто не доходит
    /// очередь, потому что молчит распознавание. Здесь очередь ни при чём —
    /// если после этой проверки экран не засветился, дело точно в отрисовке.
    /// </summary>
    public async Task RunSelfTestAsync(CancellationToken ct = default)
    {
        if (!_running)
        {
            Log.Warn("проверка свечения: оно выключено в настройках", "overlay");
            return;
        }

        Log.Info("проверка свечения: начало", "overlay");

        try
        {
            SetState(OverlayState.Sensing);
            await Task.Delay(1200, ct).ConfigureAwait(false);

            SetState(OverlayState.Listening);

            // Изображаем голос: кайма должна дышать и раскачиваться,
            // а не просто гореть ровным светом.
            for (int i = 0; i < 90 && !ct.IsCancellationRequested; i++)
            {
                SetLevel(0.35 + 0.6 * Math.Abs(Math.Sin(i * 0.19)));
                await Task.Delay(33, ct).ConfigureAwait(false);
            }

            SetLevel(0);
            SetState(OverlayState.Thinking);
            await Task.Delay(1200, ct).ConfigureAwait(false);

            SetState(OverlayState.Success);
            await Task.Delay(1200, ct).ConfigureAwait(false);

            SetState(OverlayState.Hidden);
            Log.Info("проверка свечения: конец", "overlay");
        }
        catch (OperationCanceledException)
        {
            SetState(OverlayState.Hidden);
        }
    }

    /// <summary>Сколько окон свечения реально создано. Ноль — значит, показывать нечего.</summary>
    public int BandCount => _bands.Count;

    // ---- Поток отрисовки -------------------------------------------------

    private void UiThreadBody()
    {
        try
        {
            BuildWindows();

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            _context = new ApplicationContext();
            Application.Run(_context);
        }
        catch (Exception ex)
        {
            Log.Error("поток свечения упал", ex, "overlay");
        }
        finally
        {
            try { SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; } catch { }
            DestroyWindows();
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Log.Info("конфигурация экранов изменилась, пересобираю свечение", "overlay");

        try
        {
            _anchor?.BeginInvoke(new Action(() =>
            {
                DestroyWindows();
                BuildWindows();
            }));
        }
        catch (Exception ex)
        {
            Log.Warn($"пересборка свечения не удалась: {ex.Message}", "overlay");
        }
    }

    private void BuildWindows()
    {
        var monitors = MonitorEnumerator.Enumerate();

        Log.Info($"экранов найдено: {monitors.Count}", "overlay");
        foreach (var m in monitors) Log.Info($"  {m}", "overlay");

        // «primary» — только главный, всё остальное («all», «active») строит
        // окна на всех экранах. В режиме «active» лишние гасятся при отрисовке:
        // создать окна заранее дешевле, чем пересобирать их на каждое движение мыши.
        var targets = _config.Monitors.Equals("primary", StringComparison.OrdinalIgnoreCase)
            ? monitors.Where(m => m.IsPrimary).DefaultIfEmpty(monitors[0]).ToList()
            : monitors;

        foreach (var monitor in targets)
        {
            var thickness = (int)Math.Clamp(
                Math.Min(monitor.Width, monitor.Height) * _config.Thickness,
                24, 400);

            foreach (Edge edge in Enum.GetValues<Edge>())
            {
                var bounds = BandBounds(monitor, edge, thickness);
                var screen = new Rectangle(monitor.Left, monitor.Top, monitor.Width, monitor.Height);

                var band = new GlowBandWindow(edge, bounds, screen);
                _ = band.Handle;                       // создать окно, но не показывать

                band.ApplyExcludeFromCapture(_config.ExcludeFromCapture);
                _bands.Add(band);
            }

            Log.Info($"свечение на мониторе {monitor}, толщина каймы {thickness} px", "overlay");
        }

        _anchor = _bands.FirstOrDefault();
        _colorGroup = -1;                              // заставить построить картинку при первом кадре

        if (_bands.Count == 0)
            Log.Error("не создано ни одного окна свечения — показывать будет нечего", "overlay");
    }

    private static Rectangle BandBounds(MonitorGeometry m, Edge edge, int thickness) => edge switch
    {
        Edge.Top => new Rectangle(m.Left, m.Top, m.Width, thickness),
        Edge.Bottom => new Rectangle(m.Left, m.Top + m.Height - thickness, m.Width, thickness),
        Edge.Left => new Rectangle(m.Left, m.Top, thickness, m.Height),
        Edge.Right => new Rectangle(m.Left + m.Width - thickness, m.Top, thickness, m.Height),
        _ => Rectangle.Empty,
    };

    private void DestroyWindows()
    {
        foreach (var band in _bands)
        {
            try { band.Dispose(); } catch { }
        }

        _bands.Clear();
        _anchor = null;
    }

    // ---- Кадры ------------------------------------------------------------

    private void TickerBody()
    {
        // Частоту снижаем на больших экранах: обновление слоистого окна стоит
        // передачи картинки видеокарте, и на 4K шестьдесят кадров в секунду —
        // заметный поток данных ради разницы, которую глаз почти не ловит.
        var fps = _config.TargetFps;
        var interval = TimeSpan.FromSeconds(1.0 / Math.Clamp(fps, 15, 144));

        var next = Stopwatch.GetTimestamp();
        var step = (long)(interval.TotalSeconds * Stopwatch.Frequency);

        while (_running)
        {
            try
            {
                var anchor = _anchor;

                if (anchor is not null && !anchor.IsDisposed && !_framePending)
                {
                    _framePending = true;

                    try
                    {
                        anchor.BeginInvoke(new Action(RenderFrame));
                    }
                    catch
                    {
                        _framePending = false;
                    }
                }

                next += step;
                var now = Stopwatch.GetTimestamp();
                var waitTicks = next - now;

                if (waitTicks <= 0)
                {
                    next = now;                        // отстали — не копим долг
                    continue;
                }

                var waitMs = (int)(waitTicks * 1000 / Stopwatch.Frequency);
                if (waitMs > 0) Thread.Sleep(Math.Min(waitMs, 100));
            }
            catch (Exception ex)
            {
                Log.Error("сбой в кадре свечения", ex, "overlay");
                Thread.Sleep(250);
            }
        }
    }

    private void RenderFrame()
    {
        try
        {
            if (_bands.Count == 0) return;

            var state = State;
            var level = Volatile.Read(ref _level);
            var time = _clock.Elapsed.TotalSeconds;

            // Вспышка успеха или ошибки живёт фиксированное время и сама гаснет.
            if (state is OverlayState.Success or OverlayState.Failed)
            {
                var started = Volatile.Read(ref _flashStarted);
                if (started >= 0 && time - started > FlashSeconds)
                {
                    SetState(OverlayState.Hidden);
                    state = OverlayState.Hidden;
                }
            }

            EnsureColors(state);

            // Сглаживание считаем по одному разу на сторону, а не на окно:
            // при нескольких мониторах полосы одной стороны должны дышать
            // синхронно, а не сходиться к цели вчетверо быстрее.
            for (int edge = 0; edge < 4; edge++)
            {
                var target = TargetAlpha(state, edge, time, level);

                // Вверх быстро, вниз плавно. Резкое затухание выглядит
                // как мигание лампочки, а не как дыхание.
                var coefficient = target > _current[edge] ? 0.45 : 0.12;
                _current[edge] += (target - _current[edge]) * coefficient;
            }

            var activeOnly = _config.Monitors.Equals("active", StringComparison.OrdinalIgnoreCase);
            var cursor = activeOnly ? CursorScreen() : Rectangle.Empty;

            foreach (var band in _bands)
            {
                var value = _current[(int)band.Edge];

                // В режиме активного экрана светится только тот монитор,
                // на котором сейчас указатель мыши.
                if (activeOnly && band.MonitorBounds != cursor) value = 0;

                band.SetAlpha(value < 0.004 ? 0 : value);
            }
        }
        catch (Exception ex)
        {
            Log.Error("кадр не отрисовался", ex, "overlay");
        }
        finally
        {
            _framePending = false;
        }
    }

    private Rectangle _cachedCursorScreen = Rectangle.Empty;
    private double _cursorCheckedAt = -1;

    /// <summary>
    /// Границы экрана, на котором сейчас указатель мыши.
    /// Опрашивается не чаще двух раз в секунду: положение мыши для этой
    /// задачи меняется медленно, а вызов на каждом кадре — лишняя работа.
    /// </summary>
    private Rectangle CursorScreen()
    {
        var now = _clock.Elapsed.TotalSeconds;
        if (now - _cursorCheckedAt < 0.5 && _cachedCursorScreen != Rectangle.Empty) return _cachedCursorScreen;

        _cursorCheckedAt = now;

        try
        {
            var point = Cursor.Position;

            foreach (var band in _bands)
            {
                if (band.MonitorBounds.Contains(point))
                {
                    _cachedCursorScreen = band.MonitorBounds;
                    return _cachedCursorScreen;
                }
            }
        }
        catch { /* положение мыши бывает недоступно на заблокированном экране */ }

        // Не нашли — пусть светится хоть что-то, чем ничего.
        _cachedCursorScreen = _bands.Count > 0 ? _bands[0].MonitorBounds : Rectangle.Empty;
        return _cachedCursorScreen;
    }

    /// <summary>Пересобирает картинку, только когда сменилась группа цветов.</summary>
    private void EnsureColors(OverlayState state)
    {
        var group = state switch
        {
            OverlayState.Success => 1,
            OverlayState.Failed => 2,
            _ => 0,
        };

        if (group == _colorGroup) return;
        _colorGroup = group;

        // Цвета углов идут по кругу, поэтому конец одной стороны совпадает
        // с началом следующей — переход получается без стыков.
        var corners = new[]
        {
            _palette.ColorFor(state, Edge.Top),
            _palette.ColorFor(state, Edge.Right),
            _palette.ColorFor(state, Edge.Bottom),
            _palette.ColorFor(state, Edge.Left),
        };

        foreach (var band in _bands)
        {
            var index = (int)band.Edge;
            band.Rebuild(band.BandBounds, corners[index], corners[(index + 1) % 4]);
        }
    }

    private double TargetAlpha(OverlayState state, int edge, double time, double level)
    {
        var max = _config.MaxOpacity;
        var phase = edge * (Math.PI / 2);

        switch (state)
        {
            case OverlayState.Hidden:
                return 0;

            case OverlayState.Sensing:
            {
                // Кто-то говорит, но пока неясно, к нам ли. Обозначаем присутствие
                // и не более: если это была не команда, человек почти ничего не заметит.
                var breathe = 0.72 + 0.28 * Math.Sin(time * 1.7 + phase);
                return _config.SensingOpacity * breathe;
            }

            case OverlayState.Listening:
            {
                // Основной режим. Голос — главный источник яркости, дыхание
                // держит кайму живой в паузах между словами.
                var breathe = 0.34 + 0.14 * Math.Sin(time * 2.1 + phase);
                var voice = Math.Pow(level, 0.85);
                var reactivity = _config.VoiceReactivity;

                var mixed = breathe * (1 - reactivity) + Math.Max(breathe * 0.75, voice) * reactivity;
                return max * Math.Clamp(0.28 + mixed * 0.72, 0, 1);
            }

            case OverlayState.Thinking:
            {
                // Бегущая по кругу волна: сдвиг фазы между сторонами делает так,
                // что свет обходит экран по периметру.
                var wave = 0.5 + 0.5 * Math.Sin(time * 2.8 - phase * 1.25);
                return max * (0.24 + wave * 0.34);
            }

            case OverlayState.Success:
            case OverlayState.Failed:
            {
                var started = Volatile.Read(ref _flashStarted);
                if (started < 0) return 0;

                var progress = Math.Clamp((time - started) / FlashSeconds, 0, 1);

                // Быстрый подъём, спокойное затухание.
                var envelope = progress < 0.14
                    ? progress / 0.14
                    : Math.Pow(1.0 - (progress - 0.14) / 0.86, 1.7);

                return max * Math.Clamp(envelope, 0, 1);
            }

            default:
                return 0;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_running) return;
            _running = false;
        }

        try { _ticker?.Join(500); } catch { }

        try
        {
            // Цикл сообщений останавливается только из своего же потока,
            // поэтому просьбу закрыться отправляем через окно.
            var anchor = _anchor;

            if (anchor is not null && !anchor.IsDisposed)
                anchor.BeginInvoke(new Action(Application.ExitThread));
            else
                _context?.ExitThread();
        }
        catch (Exception ex)
        {
            // Поток фоновый — если не закрылся сам, его снимет выход из программы.
            Log.Debug($"поток свечения завершился нештатно: {ex.Message}", "overlay");
        }

        try { _uiThread?.Join(1500); } catch { }

        _ticker = null;
        _uiThread = null;
        _context = null;
    }

    public void Dispose() => Stop();
}
