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

    public void Start(OverlayConfig config)
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
            _palette = new GlowPalette(config);
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

        var targets = _config.Monitors.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? monitors
            : monitors.Where(m => m.IsPrimary).DefaultIfEmpty(monitors[0]).ToList();

        foreach (var monitor in targets)
        {
            var thickness = (int)Math.Clamp(
                Math.Min(monitor.Width, monitor.Height) * _config.Thickness,
                24, 400);

            foreach (Edge edge in Enum.GetValues<Edge>())
            {
                var bounds = BandBounds(monitor, edge, thickness);

                var band = new GlowBandWindow(edge, bounds);
                _ = band.Handle;                       // создать окно, но не показывать

                band.ApplyExcludeFromCapture(_config.ExcludeFromCapture);
                _bands.Add(band);
            }

            Log.Info($"свечение на мониторе {monitor}, толщина каймы {thickness} px", "overlay");
        }

        _anchor = _bands.FirstOrDefault();
        _colorGroup = -1;                              // заставить построить картинку при первом кадре
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

            foreach (var band in _bands)
            {
                var value = _current[(int)band.Edge];
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
