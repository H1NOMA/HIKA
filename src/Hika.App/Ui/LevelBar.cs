using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hika.Ui;

/// <summary>
/// Живой уровень сигнала с микрофона.
///
/// Стоит прямо в настройках звука и отвечает на первый вопрос, который
/// возникает, когда ассистент молчит: доходит ли до него вообще звук.
/// Без этого человеку остаётся гадать, а гадать он будет неправильно.
/// </summary>
public sealed class LevelBar : Control
{
    private readonly Func<double> _source;
    private readonly System.Windows.Forms.Timer _timer;
    private double _level;
    private double _peak;

    public LevelBar(Func<double> source)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);

        _source = source;
        Size = new Size(260, 34);
        BackColor = Color.Transparent;

        _timer = new System.Windows.Forms.Timer { Interval = 60 };
        _timer.Tick += (_, _) =>
        {
            if (!Visible) return;

            _level = Math.Clamp(_source(), 0, 1);

            // Пик держится и медленно оседает — иначе короткий громкий звук
            // мелькнёт быстрее, чем глаз успеет его заметить.
            _peak = _level > _peak ? _level : Math.Max(_level, _peak - 0.012);

            Invalidate();
        };

        _timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var track = new Rectangle(0, Height / 2 - 5, Width - 1, 10);
        Theme.FillRounded(g, track, 5, Theme.Border);

        var filled = (int)(track.Width * _level);
        if (filled > 2)
        {
            // Цвет ползёт от спокойного к тревожному по мере приближения
            // к перегрузке — понятно без подписей.
            var color = _level < 0.7
                ? Theme.Accent
                : Theme.Blend(Theme.Accent, Theme.Danger, (_level - 0.7) / 0.3);

            Theme.FillRounded(g, new Rectangle(track.X, track.Y, filled, track.Height), 5, color);
        }

        var peakX = track.X + (int)(track.Width * _peak);
        if (_peak > 0.02)
        {
            using var pen = new Pen(Theme.Blend(Theme.Text, Theme.Border, 0.35), 2);
            g.DrawLine(pen, peakX, track.Y - 3, peakX, track.Bottom + 3);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
