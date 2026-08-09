using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Hika.Diagnostics;
using Hika.Interop;

namespace Hika.Overlay;

/// <summary>
/// Одна светящаяся полоса вдоль стороны экрана.
///
/// Две вещи здесь сделаны намеренно и стоят пояснения.
///
/// Первая: форма свечения рисуется один раз, а каждый кадр меняются только
/// прозрачность окна и точка чтения из картинки. Благодаря этому кайма
/// отзывается на голос мгновенно и переливается, а перерисовки не происходит
/// вовсе — в покое отрисовка не стоит ничего.
///
/// Вторая: картинка вдвое длиннее самой полосы и содержит непрерывный цикл
/// цветов. Смещая точку чтения, мы гоним цвета вдоль края — тот самый
/// перелив, — и это обходится в одно целое число за кадр вместо перерисовки
/// миллиона пикселей.
///
/// Окно сквозное для мыши, не забирает фокус и не показывается ни в панели
/// задач, ни в Alt+Tab: человек должен видеть свечение и не замечать окна.
/// </summary>
internal sealed class GlowBandWindow : Form
{
    private readonly Edge _edge;

    private Rectangle _bounds;
    private IntPtr _memoryDc = IntPtr.Zero;
    private IntPtr _bitmapHandle = IntPtr.Zero;
    private IntPtr _previousBitmap = IntPtr.Zero;

    /// <summary>Во сколько раз картинка длиннее полосы. Запас нужен для прокрутки цветов.</summary>
    private const int CycleFactor = 2;

    private int _cycleLength;

    // Сознательно int, а не byte: −1 означает «ещё ничего не рисовали».
    // С byte-сентинелом пришлось бы занять какое-то настоящее значение
    // прозрачности, и кадр ровно с ним молча терялся бы.
    private int _lastAlpha = -1;
    private int _lastOffset = -1;

    private bool _visible;
    private bool _surfaceReady;
    private bool _failureLogged;

    public Edge Edge => _edge;

    /// <summary>Ноль — ни один кадр так и не отрисовался. Для проверки свечения.</summary>
    public int FramesDrawn { get; private set; }

    /// <summary>Код последней ошибки Windows при отрисовке, ноль — ошибок не было.</summary>
    public int LastError { get; private set; }

    /// <summary>
    /// Положение полосы в физических пикселях рабочего стола.
    ///
    /// Намеренно отдельно от <see cref="Form.Bounds"/>: окно позиционируется
    /// не средствами форм, а самим вызовом UpdateLayeredWindow, и координаты
    /// здесь — настоящие пиксели, не тронутые масштабированием интерфейса.
    /// </summary>
    public Rectangle BandBounds => _bounds;

    /// <summary>Границы монитора, к которому относится полоса — для режима «активный экран».</summary>
    public Rectangle MonitorBounds { get; }

    public GlowBandWindow(Edge edge, Rectangle bounds, Rectangle monitorBounds)
    {
        _edge = edge;
        _bounds = bounds;
        MonitorBounds = monitorBounds;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Text = "";

        // Ни Opacity, ни AllowTransparency трогать нельзя, и это не мелочь.
        // Оба заставляют WinForms вызвать SetLayeredWindowAttributes, а этот
        // вызов и UpdateLayeredWindow взаимоисключающие: после первого второй
        // навсегда возвращает ошибку. Окно при этом остаётся пустым и молчит.
        // Именно на этом свечение и не появлялось.

        // Ничего не рисуем средствами форм: содержимое окна целиком задаётся
        // через UpdateLayeredWindow, и любая попытка WinForms закрасить фон
        // только испортит попиксельную прозрачность.
        SetStyle(ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint, true);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= (int)(Win32.WS_EX_LAYERED
                               | Win32.WS_EX_TRANSPARENT
                               | Win32.WS_EX_TOOLWINDOW
                               | Win32.WS_EX_NOACTIVATE
                               | Win32.WS_EX_TOPMOST);
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // Страховка от того же самого: если WinForms всё-таки успела применить
        // к окну прозрачность своим способом, снятие и возврат стиля сбрасывает
        // это состояние, и UpdateLayeredWindow снова становится доступен.
        try
        {
            var style = Win32.GetWindowLongAuto(Handle, Win32.GWL_EXSTYLE).ToInt64();

            Win32.SetWindowLongAuto(Handle, Win32.GWL_EXSTYLE, new IntPtr(style & ~Win32.WS_EX_LAYERED));
            Win32.SetWindowLongAuto(Handle, Win32.GWL_EXSTYLE, new IntPtr(style | Win32.WS_EX_LAYERED));
        }
        catch (Exception ex)
        {
            Log.Debug($"сброс состояния прозрачности не удался: {ex.Message}", "overlay");
        }
    }

    /// <summary>Окно не должно всплывать при показе — фокус остаётся у того, с чем человек работает.</summary>
    protected override bool ShowWithoutActivation => true;

    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    public void ApplyExcludeFromCapture(bool exclude)
    {
        try
        {
            Win32.SetWindowDisplayAffinity(Handle, exclude ? Win32.WDA_EXCLUDEFROMCAPTURE : Win32.WDA_NONE);
        }
        catch (Exception ex)
        {
            Log.Debug($"скрытие от захвата экрана недоступно: {ex.Message}", "overlay");
        }
    }

    /// <summary>
    /// Пересоздаёт картинку свечения. Вызывается при смене состояния,
    /// разрешения или палитры — но не каждый кадр.
    /// </summary>
    public void Rebuild(Rectangle bounds, Color[] palette)
    {
        _bounds = bounds;
        ReleaseSurface();

        var horizontal = _edge is Edge.Top or Edge.Bottom;

        var bandLength = Math.Max(1, horizontal ? bounds.Width : bounds.Height);
        var depth = Math.Max(1, horizontal ? bounds.Height : bounds.Width);

        _cycleLength = bandLength;

        var width = horizontal ? bandLength * CycleFactor : depth;
        var height = horizontal ? depth : bandLength * CycleFactor;

        try
        {
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            DrawGlow(bitmap, palette, horizontal, depth, bandLength);

            // Нулевой фон здесь принципиален: только так GetHbitmap сохраняет
            // попиксельную прозрачность, а не смешивает её с цветом подложки.
            _bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));

            var screenDc = Win32.GetDC(IntPtr.Zero);
            try
            {
                _memoryDc = Win32.CreateCompatibleDC(screenDc);
                _previousBitmap = Win32.SelectObject(_memoryDc, _bitmapHandle);
            }
            finally
            {
                Win32.ReleaseDC(IntPtr.Zero, screenDc);
            }

            _surfaceReady = true;
            _lastAlpha = -1;
            _lastOffset = -1;
        }
        catch (Exception ex)
        {
            Log.Error($"не удалось построить свечение для стороны {_edge}", ex, "overlay");
            _surfaceReady = false;
        }
    }

    /// <summary>
    /// Рисует профиль яркости поперёк полосы и непрерывный цикл цветов вдоль неё.
    ///
    /// Профиль прижат к кромке. Первая попытка была пологой — казалось, что
    /// широкий ореол выглядит мягче резкой линии у края. На экране вышло
    /// наоборот: свет расползался вглубь и читался как засветка, а не как
    /// подсветка края. Теперь основная доля яркости отдана крутой
    /// составляющей, и видимый свет живёт во внешней трети полосы; пологая
    /// часть осталась только затем, чтобы граница света не была заметна.
    ///
    /// Отсюда же и цифры в настройках: толщина задаёт всю полосу, а глазом
    /// видно заметно меньше.
    /// </summary>
    private void DrawGlow(Bitmap bitmap, Color[] palette, bool horizontal, int depth, int bandLength)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;

        // Профиль поперёк полосы — по одному значению на строку или столбец.
        var profile = new double[depth];
        for (int d = 0; d < depth; d++)
        {
            // t = 1 у самой кромки экрана, 0 — в глубине.
            var raw = (d + 0.5) / depth;
            var t = _edge is Edge.Top or Edge.Left ? 1.0 - raw : raw;

            profile[d] = Math.Clamp(0.78 * Math.Pow(t, 5.0) + 0.22 * Math.Pow(t, 2.2), 0, 1);
        }

        // Цвета вдоль полосы считаем заранее: во внутреннем цикле остаётся
        // только выборка и умножение, иначе на 4K сборка картинки заметно тормозит.
        var lengthPixels = horizontal ? width : height;
        var colors = new Color[lengthPixels];

        // Полтора оборота палитры на длину полосы: на каждой стороне видно
        // несколько оттенков сразу, а не один сплошной цвет.
        const double Cycles = 1.5;

        for (int i = 0; i < lengthPixels; i++)
        {
            // Позиция считается по длине полосы, а не картинки, — тогда при
            // прокрутке цвета переходят через край без стыка.
            var position = (i % Math.Max(1, bandLength)) / (double)Math.Max(1, bandLength);
            colors[i] = SamplePalette(palette, position * Cycles);
        }

        var data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppPArgb);

        try
        {
            unsafe
            {
                var scan0 = (byte*)data.Scan0;

                for (int y = 0; y < height; y++)
                {
                    var row = scan0 + y * data.Stride;
                    var rowColor = horizontal ? Color.Empty : colors[y];
                    var rowAlpha = horizontal ? profile[Math.Min(y, depth - 1)] : 0;

                    for (int x = 0; x < width; x++)
                    {
                        var color = horizontal ? colors[x] : rowColor;
                        var alpha = horizontal ? rowAlpha : profile[Math.Min(x, depth - 1)];

                        var a = (byte)(alpha * 255.0);

                        // Формат premultiplied: цвет уже умножен на прозрачность.
                        var pixel = row + x * 4;
                        pixel[0] = (byte)(color.B * a / 255);
                        pixel[1] = (byte)(color.G * a / 255);
                        pixel[2] = (byte)(color.R * a / 255);
                        pixel[3] = a;
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>Цвет в точке замкнутой палитры. Дробная часть позиции — доля пути между соседними цветами.</summary>
    private static Color SamplePalette(Color[] palette, double position)
    {
        if (palette.Length == 0) return Color.White;
        if (palette.Length == 1) return palette[0];

        var scaled = position * palette.Length;
        var index = (int)Math.Floor(scaled);
        var fraction = scaled - index;

        var from = palette[((index % palette.Length) + palette.Length) % palette.Length];
        var to = palette[(((index + 1) % palette.Length) + palette.Length) % palette.Length];

        // Сглаживание перехода: линейная доля даёт видимые полосы на стыках.
        fraction = fraction * fraction * (3 - 2 * fraction);

        return Color.FromArgb(
            (int)(from.R + (to.R - from.R) * fraction),
            (int)(from.G + (to.G - from.G) * fraction),
            (int)(from.B + (to.B - from.B) * fraction));
    }

    /// <summary>
    /// Задаёт яркость полосы и смещение цветов. Ноль яркости — окно скрывается.
    /// </summary>
    /// <param name="flow">Доля прокрутки цветов, 0..1. Именно она даёт перелив.</param>
    public void SetFrame(double alpha, double flow)
    {
        if (!_surfaceReady || IsDisposed) return;

        var value = (int)Math.Clamp(Math.Round(alpha * 255.0), 0, 255);

        if (value == 0)
        {
            HideBand();
            return;
        }

        var offset = _cycleLength <= 0
            ? 0
            : (int)(((flow % 1.0) + 1.0) % 1.0 * _cycleLength);

        // Разница меньше одного шага прозрачности глазу недоступна, а обновление
        // слоистого окна стоит передачи картинки видеокарте. В покое это
        // экономит вообще всё.
        if (_visible && value == _lastAlpha && offset == _lastOffset) return;

        _lastAlpha = value;
        _lastOffset = offset;

        var screenDc = Win32.GetDC(IntPtr.Zero);
        try
        {
            var destination = new Win32.POINT { X = _bounds.X, Y = _bounds.Y };
            var size = new Win32.SIZE { Cx = _bounds.Width, Cy = _bounds.Height };

            var horizontal = _edge is Edge.Top or Edge.Bottom;
            var source = new Win32.POINT
            {
                X = horizontal ? offset : 0,
                Y = horizontal ? 0 : offset,
            };

            var blend = new Win32.BLENDFUNCTION
            {
                BlendOp = Win32.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = (byte)value,
                AlphaFormat = Win32.AC_SRC_ALPHA,
            };

            // Содержимое задаётся до показа окна: иначе в первом кадре
            // мелькнёт то, что осталось в памяти видеокарты.
            var ok = Win32.UpdateLayeredWindow(
                Handle, screenDc, ref destination, ref size,
                _memoryDc, ref source, 0, ref blend, Win32.ULW_ALPHA);

            if (!ok)
            {
                LastError = Marshal.GetLastWin32Error();

                // Один раз, а не каждый кадр: иначе журнал за минуту вырастет
                // до сотен мегабайт и утащит за собой диск.
                if (!_failureLogged)
                {
                    _failureLogged = true;
                    Log.Error($"отрисовка свечения ({_edge}) отклонена Windows, код {LastError}", "overlay");
                }

                return;
            }

            LastError = 0;
            FramesDrawn++;

            if (!_visible)
            {
                Win32.ShowWindow(Handle, Win32.SW_SHOWNOACTIVATE);
                Win32.SetWindowPos(Handle, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                    Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);

                _visible = true;
            }
        }
        catch (Exception ex)
        {
            if (!_failureLogged)
            {
                _failureLogged = true;
                Log.Error($"сбой отрисовки свечения ({_edge})", ex, "overlay");
            }
        }
        finally
        {
            Win32.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public void HideBand()
    {
        if (!_visible || IsDisposed) return;

        _visible = false;
        _lastAlpha = -1;

        try { Win32.ShowWindow(Handle, Win32.SW_HIDE); } catch { }
    }

    private void ReleaseSurface()
    {
        _surfaceReady = false;

        try
        {
            if (_memoryDc != IntPtr.Zero)
            {
                if (_previousBitmap != IntPtr.Zero) Win32.SelectObject(_memoryDc, _previousBitmap);
                Win32.DeleteDC(_memoryDc);
            }

            if (_bitmapHandle != IntPtr.Zero) Win32.DeleteObject(_bitmapHandle);
        }
        catch { /* при выгрузке уже неважно */ }

        _memoryDc = IntPtr.Zero;
        _bitmapHandle = IntPtr.Zero;
        _previousBitmap = IntPtr.Zero;
    }

    protected override void Dispose(bool disposing)
    {
        ReleaseSurface();
        base.Dispose(disposing);
    }
}
