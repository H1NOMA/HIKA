using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Hika.Nlu;

namespace Hika.Ui;

/// <summary>
/// Всё, что ей можно сказать, — списком.
///
/// Полтораста команд невозможно помнить, а спросить не у кого. Отправлять
/// за этим в документацию бессмысленно: человек ставил программу, а не
/// подписывался на чтение репозитория.
///
/// Рисуется одним элементом, а не полусотней: пятьдесят строк, каждая
/// из которых отдельное окно Windows, — это заметная пауза при открытии
/// раздела и мерцание при прокрутке.
/// </summary>
public sealed class CommandBook : Control
{
    private const int GroupTop = 26;
    private const int GroupTitle = 26;
    private const int HintHeight = 34;
    private const int RowHeight = 28;

    public CommandBook()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer, true);

        BackColor = Theme.Panel;
        Height = Measure();
    }

    private static int Measure()
    {
        var height = 0;

        foreach (var group in CommandExamples.All)
        {
            height += GroupTop + GroupTitle;
            if (group.Hint.Length > 0) height += HintHeight;
            height += group.Examples.Count * RowHeight;
        }

        return height + 24;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        // Ширина фразы — примерно половина, но не больше того, что нужно
        // самой длинной: иначе в узком окне пояснения наезжают на команды.
        var sayWidth = Math.Clamp(Width / 2, 180, 340);

        var y = 0;

        foreach (var group in CommandExamples.All)
        {
            y += GroupTop;

            TextRenderer.DrawText(g, group.Title, Theme.Section,
                new Rectangle(0, y, Width, GroupTitle), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.Top);

            y += GroupTitle;

            if (group.Hint.Length > 0)
            {
                TextRenderer.DrawText(g, group.Hint, Theme.Small,
                    new Rectangle(0, y - 2, Math.Max(200, Width - 20), HintHeight), Theme.TextFaint,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);

                y += HintHeight;
            }

            foreach (var example in group.Examples)
            {
                var row = new Rectangle(0, y, Width, RowHeight);

                Theme.FillRounded(g, new Rectangle(0, row.Y + 4, 3, RowHeight - 10), 2,
                    Theme.Blend(Theme.Accent, Theme.Panel, 0.45));

                TextRenderer.DrawText(g, "«" + example.Say + "»", Theme.Body,
                    new Rectangle(14, row.Y, sayWidth, RowHeight), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                if (example.Does.Length > 0)
                {
                    TextRenderer.DrawText(g, example.Does, Theme.Small,
                        new Rectangle(sayWidth + 26, row.Y, Math.Max(80, Width - sayWidth - 34), RowHeight),
                        Theme.TextFaint,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }

                y += RowHeight;
            }
        }
    }
}
