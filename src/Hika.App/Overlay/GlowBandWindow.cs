using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Hika.Diagnostics;
using Hika.Interop;

namespace Hika.Overlay;

/// <summary>
/// Одна светящаяся полоса вдоль стороны экрана.
///
/// Устройство намеренно устроено так, что форма свечения рисуется один раз,
/// а каждый кадр меняется только общая прозрачность окна. Голос управляет
/// именно ей — благодаря этому кайма отзывается на речь мгновенно, а
/// перерисовки картинки при этом не происходит вовсе.
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

    // Сознательно int, а не byte: −1 означает «ещё ничего не рисовали».
    // С byte-сентинелом пришлось бы занять какое-то настоящее значение
    // прозрачности, и кадр ровно с ним молча терялся бы.
    private int _lastAlpha = -1;

    private bool _visible;
    private bool _surfaceReady;

    public Edge Edge => _edge;

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
        Opacity = 1.0;

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
    public void Rebuild(Rectangle bounds, Color from, Color to)
    {
        _bounds = bounds;
        ReleaseSurface();

        var width = Math.Max(1, bounds.Width);
        var height = Math.Max(1, bounds.Height);

        try
        {
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            DrawGlow(bitmap, from, to);

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
        }
        catch (Exception ex)
        {
            Log.Error($"не удалось построить свечение для стороны {_edge}", ex, "overlay");
            _surfaceReady = false;
        }
    }

    /// <summary>
    /// Рисует профиль яркости поперёк полосы и переход цвета вдоль неё.
    ///
    /// Профиль — сумма узкой яркой линии у самого края и широкого мягкого ореола.
    /// Одна лишь узкая линия выглядит как подсветка монитора, один лишь ореол —
    /// как мутное пятно; вместе они дают ту самую каёмку.
    /// </summary>
    private void DrawGlow(Bitmap bitmap, Color from, Color to)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var horizontal = _edge is Edge.Top or Edge.Bottom;
        var depth = horizontal ? height : width;
        if (depth <= 0) return;

        var data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppPArgb);

        try
        {
            unsafe
            {
                var scan0 = (byte*)data.Scan0;

                // Профиль поперёк полосы считаем один раз на строку/столбец.
                var profile = new double[depth];
                for (int d = 0; d < depth; d++)
                {
                    // t = 1 у самой кромки экрана, 0 — в глубине.
                    var raw = (d + 0.5) / depth;
                    var t = _edge is Edge.Top or Edge.Left ? 1.0 - raw : raw;

                    profile[d] = Math.Clamp(0.82 * Math.Pow(t, 2.6) + 0.18 * Math.Pow(t, 0.9), 0, 1);
                }

                for (int y = 0; y < height; y++)
                {
                    var row = scan0 + y * data.Stride;

                    for (int x = 0; x < width; x++)
                    {
                        var d = horizontal ? y : x;
                        var alpha = profile[d];

                        // Доля пути вдоль полосы — по ней смешиваются цвета углов.
                        var along = ColorPosition(x, y, width, height);

                        var r = (byte)(from.R + (to.R - from.R) * along);
                        var g = (byte)(from.G + (to.G - from.G) * along);
                        var b = (byte)(from.B + (to.B - from.B) * along);

                        var a = (byte)(alpha * 255.0);

                        // Формат premultiplied: цвет уже умножен на прозрачность.
                        var pixel = row + x * 4;
                        pixel[0] = (byte)(b * a / 255);
                        pixel[1] = (byte)(g * a / 255);
                        pixel[2] = (byte)(r * a / 255);
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

    /// <summary>
    /// Положение точки вдоль полосы, 0..1. Направления подобраны так, чтобы
    /// цвета переходили друг в друга непрерывно по кругу: верх слева направо,
    /// правая сторона сверху вниз, низ справа налево, левая снизу вверх.
    /// </summary>
    private double ColorPosition(int x, int y, int width, int height) => _edge switch
    {
        Edge.Top => width <= 1 ? 0 : (double)x / (width - 1),
        Edge.Right => height <= 1 ? 0 : (double)y / (height - 1),
        Edge.Bottom => width <= 1 ? 0 : 1.0 - (double)x / (width - 1),
        Edge.Left => height <= 1 ? 0 : 1.0 - (double)y / (height - 1),
        _ => 0,
    };

    /// <summary>Задаёт яркость полосы. Ноль — окно скрывается.</summary>
    public void SetAlpha(double alpha)
    {
        if (!_surfaceReady || IsDisposed) return;

        var value = (int)Math.Clamp(Math.Round(alpha * 255.0), 0, 255);

        if (value == 0)
        {
            HideBand();
            return;
        }

        if (!_visible)
        {
            Win32.ShowWindow(Handle, Win32.SW_SHOWNOACTIVATE);
            Win32.SetWindowPos(Handle, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            _visible = true;
            _lastAlpha = -1;                 // первый показ обязан отрисоваться
        }

        // Разница меньше одного шага прозрачности глазу недоступна, а обновление
        // слоистого окна стоит передачи всей картинки видеокарте. В покое
        // это экономит вообще всё.
        if (value == _lastAlpha) return;
        _lastAlpha = value;

        var screenDc = Win32.GetDC(IntPtr.Zero);
        try
        {
            var destination = new Win32.POINT { X = _bounds.X, Y = _bounds.Y };
            var size = new Win32.SIZE { Cx = _bounds.Width, Cy = _bounds.Height };
            var source = new Win32.POINT { X = 0, Y = 0 };

            var blend = new Win32.BLENDFUNCTION
            {
                BlendOp = Win32.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = (byte)value,
                AlphaFormat = Win32.AC_SRC_ALPHA,
            };

            Win32.UpdateLayeredWindow(
                Handle, screenDc, ref destination, ref size,
                _memoryDc, ref source, 0, ref blend, Win32.ULW_ALPHA);
        }
        catch (Exception ex)
        {
            Log.Debug($"обновление свечения не прошло: {ex.Message}", "overlay");
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
