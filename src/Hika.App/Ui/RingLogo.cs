using System.Drawing;
using System.Drawing.Drawing2D;

namespace Hika.Ui;

/// <summary>
/// Знак HIKA: кольцо с точкой внутри.
///
/// Рисуется кодом, а не берётся из файла, потому что должен менять цвет
/// вместе с выбранной личностью и подсвечиваться в такт состоянию. Один
/// и тот же код рисует и значок возле часов, и заголовок окна настроек —
/// поэтому они не могут разойтись.
/// </summary>
public static class RingLogo
{
    /// <param name="glow">0..1 — насколько ярко светится ореол вокруг кольца.</param>
    /// <param name="fillCore">Закрашивать точку в центре.</param>
    public static void Draw(Graphics g, RectangleF bounds, Color accent, double glow = 0, bool fillCore = true)
    {
        var previousMode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        try
        {
            var size = Math.Min(bounds.Width, bounds.Height);
            var cx = bounds.X + bounds.Width / 2f;
            var cy = bounds.Y + bounds.Height / 2f;

            // Ореол — несколько расширяющихся полупрозрачных колец.
            // Радиальная кисть дала бы то же самое, но с ней сложнее
            // управлять затуханием, а колец достаточно трёх.
            if (glow > 0.01)
            {
                for (int i = 3; i >= 1; i--)
                {
                    var spread = size * (0.36f + i * 0.085f);
                    var alpha = (int)(glow * 46 / i);
                    if (alpha <= 0) continue;

                    using var haloPen = new Pen(Color.FromArgb(alpha, accent), size * 0.10f);
                    g.DrawEllipse(haloPen, cx - spread, cy - spread, spread * 2, spread * 2);
                }
            }

            var ringRadius = size * 0.355f;
            var ringWidth = size * 0.115f;

            using (var pen = new Pen(accent, ringWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawEllipse(pen, cx - ringRadius, cy - ringRadius, ringRadius * 2, ringRadius * 2);
            }

            if (fillCore)
            {
                var coreRadius = size * 0.135f;

                // Ядро светлее кольца — так знак не выглядит плоским пятном.
                var core = Theme.Blend(accent, Color.White, 0.42);
                using var brush = new SolidBrush(core);
                g.FillEllipse(brush, cx - coreRadius, cy - coreRadius, coreRadius * 2, coreRadius * 2);
            }
        }
        finally
        {
            g.SmoothingMode = previousMode;
        }
    }

    /// <summary>Значок для трея. Вызывающий обязан освободить результат.</summary>
    public static Icon CreateIcon(Color accent, bool muted, int size = 32)
    {
        using var bitmap = new Bitmap(size, size);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var color = muted ? Theme.Blend(accent, Color.FromArgb(0x60, 0x68, 0x78), 0.72) : accent;
            Draw(g, new RectangleF(1, 1, size - 2, size - 2), color, glow: 0, fillCore: !muted);

            if (muted)
            {
                // Перечёркнутый знак читается как «выключено» без единой надписи.
                using var pen = new Pen(Theme.Blend(color, Color.White, 0.35f), size * 0.09f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                };

                g.DrawLine(pen, size * 0.24f, size * 0.76f, size * 0.76f, size * 0.24f);
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        internal static extern bool DestroyIcon(IntPtr handle);
    }
}
