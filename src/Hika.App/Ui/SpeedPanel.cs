using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Hika.Diagnostics;

namespace Hika.Ui;

/// <summary>
/// Из чего складывается ожидание — полосой, а не таблицей.
///
/// «Медленно» — не диагноз, а жалоба. Полторы секунды могут быть полутора
/// секундами распознавания, а могут — четырьмя сотнями миллисекунд ожидания
/// вашей паузы плюс тем же распознаванием, и лечится это в разных местах.
/// Три цвета в одной полосе отвечают на этот вопрос быстрее любых чисел:
/// видно, какая часть длиннее, и понятно, куда смотреть.
///
/// Числа под полосой всё равно есть — но они уже уточнение, а не загадка.
/// </summary>
public sealed class SpeedChart : Control
{
    private readonly Func<SpeedSummary?> _source;
    private readonly System.Windows.Forms.Timer _timer;

    private SpeedSummary? _summary;

    public SpeedChart(Func<SpeedSummary?> source)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer, true);

        _source = source;
        Height = 148;
        BackColor = Theme.Panel;

        // Раз в секунду: числа меняются от команды к команде, а не от кадра
        // к кадру, и перерисовывать чаще незачем.
        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) =>
        {
            if (!Visible) return;

            var next = Read();
            if (Same(next, _summary)) return;

            _summary = next;
            Invalidate();
        };
        _timer.Start();

        _summary = Read();
    }

    private SpeedSummary? Read()
    {
        try { return _source(); }
        catch { return null; }
    }

    private static bool Same(SpeedSummary? a, SpeedSummary? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;

        return a.Value.Commands == b.Value.Commands
            && a.Value.TotalMs == b.Value.TotalMs
            && a.Value.WakeMs == b.Value.WakeMs;
    }

    private static readonly Color Silence = Color.FromArgb(0x4A, 0x55, 0x66);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        if (_summary is not { Commands: > 0 } summary)
        {
            TextRenderer.DrawText(g, "Скажите команду — и здесь появится, из чего сложилось ожидание.",
                Theme.Body, new Rectangle(0, 20, Width, 24), Theme.TextFaint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            return;
        }

        var total = Math.Max(1, summary.TotalMs);

        TextRenderer.DrawText(g, $"Обычно вы ждёте {SpeedAdvice.Text(total)}",
            Theme.Title, new Rectangle(0, 2, Width, 30), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        TextRenderer.DrawText(g, $"после того, как договорили  ·  по последним {summary.Commands}",
            Theme.Small, new Rectangle(0, 32, Width, 18), Theme.TextFaint,
            TextFormatFlags.Left | TextFormatFlags.Top);

        // ---- Полоса ---------------------------------------------------------

        var bar = new Rectangle(0, 58, Math.Max(60, Width - 8), 22);
        Theme.FillRounded(g, bar, 6, Theme.Card);

        var parts = new (int Ms, Color Color, string Name)[]
        {
            (summary.SilenceMs, Silence, "ждала конца фразы"),
            (summary.RecognitionMs, Theme.Accent, "распознавание"),
            (summary.ActionMs, Theme.Blend(Theme.Accent, Theme.Good, 0.75), "действие"),
        };

        var x = bar.X;
        for (int i = 0; i < parts.Length; i++)
        {
            var (ms, color, _) = parts[i];

            // Последний кусок дотягивается до края: округление трёх долей
            // оставляет щель в пару пикселей, и она читается как дефект.
            var width = i == parts.Length - 1
                ? bar.Right - x
                : (int)Math.Round(bar.Width * (ms / (double)total));

            width = Math.Min(width, bar.Right - x);
            if (width <= 0) continue;

            using var brush = new SolidBrush(color);
            g.FillRectangle(brush, x, bar.Y, width, bar.Height);

            x += width;
        }

        // Скругление краёв поверх прямоугольников — дешевле, чем считать
        // скруглённый путь для каждого куска отдельно.
        using (var pen = new Pen(Theme.Panel, 3))
        {
            g.DrawLine(pen, bar.X, bar.Y, bar.X, bar.Bottom);
            g.DrawLine(pen, bar.Right - 1, bar.Y, bar.Right - 1, bar.Bottom);
        }

        Theme.DrawRounded(g, bar, 6, Theme.Border);

        // ---- Подписи ---------------------------------------------------------

        var legendY = bar.Bottom + 12;
        var legendX = 0;

        foreach (var (ms, color, name) in parts)
        {
            var label = $"{name} {SpeedAdvice.Text(ms)}";
            var size = TextRenderer.MeasureText(g, label, Theme.Small);

            using (var dot = new SolidBrush(color)) g.FillEllipse(dot, legendX, legendY + 4, 9, 9);

            TextRenderer.DrawText(g, label, Theme.Small,
                new Rectangle(legendX + 14, legendY, size.Width + 4, 18), Theme.TextDim,
                TextFormatFlags.Left | TextFormatFlags.Top);

            legendX += size.Width + 34;
        }

        // ---- Две строки, которые не влезли в полосу ---------------------------

        var wake = summary.WakeMs > 0
            ? $"Кайма загорается через {SpeedAdvice.Text(summary.WakeMs)} после начала фразы."
            : "Кайма в этих командах не измерялась — имя в них не проверялось.";

        var speed = summary.RealTime > 0
            ? summary.RealTime <= 1
                ? $" Распознавание идёт в {1 / summary.RealTime:0.0} раза быстрее речи — запас есть."
                : $" Распознавание идёт в {summary.RealTime:0.0} раза дольше речи — запаса нет."
            : "";

        TextRenderer.DrawText(g, wake + speed, Theme.Small,
            new Rectangle(0, legendY + 26, Width - 8, 36), Theme.TextFaint,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Приговор скорости и одна кнопка, которая его исправляет.
///
/// Существует потому, что вопрос «почему медленно» человек задаёт программе,
/// а не себе. Показать ему числа и оставить наедине с девятью ползунками —
/// значит переложить свою работу на того, кто заведомо не знает, какой
/// из них тут виноват. Программа знает.
/// </summary>
public sealed class AdviceRow : Control
{
    private readonly Func<SpeedAdvice?> _source;
    private readonly FlatButton _button;
    private readonly System.Windows.Forms.Timer _timer;

    private SpeedAdvice? _advice;

    public AdviceRow(Func<SpeedAdvice?> source, Action<SpeedAdvice> apply)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer, true);

        _source = source;
        Height = 126;
        BackColor = Theme.Panel;

        _button = new FlatButton("", primary: true) { Width = 240, Height = 34, Visible = false };
        _button.Click += (_, _) =>
        {
            if (_advice is { Actionable: true } advice) apply(advice);
        };

        Controls.Add(_button);

        _timer = new System.Windows.Forms.Timer { Interval = 1500 };
        _timer.Tick += (_, _) => { if (Visible) Refresh(); };
        _timer.Start();

        Refresh();
    }

    /// <summary>Перечитывает приговор. Зовётся и снаружи — сразу после применения правки.</summary>
    public new void Refresh()
    {
        SpeedAdvice? next;
        try { next = _source(); }
        catch { next = null; }

        if (next?.Verdict == _advice?.Verdict && next?.Detail == _advice?.Detail) return;

        _advice = next;

        _button.Text = next?.FixLabel ?? "";
        _button.Visible = next is { Actionable: true };

        PlaceButton();
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        PlaceButton();
    }

    private void PlaceButton()
        => _button.Location = new Point(Math.Max(12, Width - _button.Width - 20), Height - _button.Height - 16);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        if (_advice is null) return;

        var card = new Rectangle(0, 0, Math.Max(80, Width - 8), Height - 10);

        var tint = _advice.Slow ? Theme.Warn : _advice.Actionable ? Theme.Accent : Theme.Good;

        Theme.FillRounded(g, card, 10, Theme.Blend(Theme.Card, tint, 0.07));
        Theme.DrawRounded(g, card, 10, Theme.Blend(Theme.Border, tint, 0.45));

        Theme.FillRounded(g, new Rectangle(card.X, card.Y + 12, 3, card.Height - 24), 2, tint);

        TextRenderer.DrawText(g, _advice.Verdict, Theme.BodyBold,
            new Rectangle(18, 12, card.Width - 36, 22), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);

        // Ширину текста ограничиваем кнопкой, иначе он уедет под неё.
        var textWidth = _button.Visible ? card.Width - _button.Width - 44 : card.Width - 36;

        TextRenderer.DrawText(g, _advice.Detail, Theme.Small,
            new Rectangle(18, 36, Math.Max(120, textWidth), card.Height - 46), Theme.TextDim,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Последние услышанные фразы и что с каждой стало.
///
/// Отвечает на «почему она открыла не то» — вопрос, на который до сих пор
/// нельзя было ответить, не открыв консоль. Причина такой ошибки всегда одна
/// из трёх: расслышала не то, разобрала не так, нашла не ту программу.
/// Порознь они неразличимы, рядом — очевидны.
/// </summary>
public sealed class HeardList : Control
{
    private readonly Func<IReadOnlyList<Heard>> _source;
    private readonly System.Windows.Forms.Timer _timer;

    private IReadOnlyList<Heard> _items = Array.Empty<Heard>();

    private const int RowHeight = 44;

    public HeardList(Func<IReadOnlyList<Heard>> source)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer, true);

        _source = source;
        Height = RowHeight * 8;
        BackColor = Theme.Panel;

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) =>
        {
            if (!Visible) return;

            var next = Read();
            if (next.Count == _items.Count && (next.Count == 0 || ReferenceEquals(next[0], _items[0]))) return;

            _items = next;
            Height = Math.Max(RowHeight, RowHeight * Math.Max(1, next.Count));
            Invalidate();
        };
        _timer.Start();

        _items = Read();
    }

    private IReadOnlyList<Heard> Read()
    {
        try { return _source(); }
        catch { return Array.Empty<Heard>(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        if (_items.Count == 0)
        {
            TextRenderer.DrawText(g, "Пока ничего не слышала.",
                Theme.Body, new Rectangle(0, 8, Width, 24), Theme.TextFaint,
                TextFormatFlags.Left | TextFormatFlags.Top);
            return;
        }

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var top = i * RowHeight;

            var (color, mark) = Describe(item.Outcome);

            // Полоска цвета слева — по ней список читается одним взглядом,
            // без чтения текста.
            Theme.FillRounded(g, new Rectangle(0, top + 8, 3, RowHeight - 18), 2, color);

            var text = string.IsNullOrWhiteSpace(item.Text) ? "(тишина)" : item.Text;

            TextRenderer.DrawText(g, "«" + text + "»", Theme.Body,
                new Rectangle(14, top + 6, Width - 120, 20), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);

            var detail = string.IsNullOrWhiteSpace(item.Result)
                ? $"{mark} · {item.Intent}"
                : $"{mark} · {item.Intent} → {item.Result}";

            TextRenderer.DrawText(g, detail, Theme.Small,
                new Rectangle(14, top + 24, Width - 120, 18), Theme.TextFaint,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);

            TextRenderer.DrawText(g, SpeedAdvice.Text(item.TotalMs), Theme.Small,
                new Rectangle(Width - 100, top + 6, 96, 20), Theme.TextFaint,
                TextFormatFlags.Right | TextFormatFlags.Top);

            if (item.Outcome == HeardOutcome.NotForUs && item.WakeScore > 0.2)
            {
                TextRenderer.DrawText(g, $"похоже на имя: {item.WakeScore:0.00}", Theme.Small,
                    new Rectangle(Width - 160, top + 24, 156, 18), Theme.Warn,
                    TextFormatFlags.Right | TextFormatFlags.Top);
            }

            using var pen = new Pen(Theme.Border);
            g.DrawLine(pen, 0, top + RowHeight - 1, Width, top + RowHeight - 1);
        }
    }

    private static (Color Color, string Mark) Describe(HeardOutcome outcome) => outcome switch
    {
        HeardOutcome.Done => (Theme.Good, "выполнено"),
        HeardOutcome.Failed => (Theme.Danger, "не вышло"),
        HeardOutcome.NotUnderstood => (Theme.Warn, "не разобрала"),
        HeardOutcome.Talk => (Theme.Accent, "разговор"),
        _ => (Theme.Border, "не мне"),
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }
}
