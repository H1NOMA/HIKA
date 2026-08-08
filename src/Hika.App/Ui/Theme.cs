using System.Drawing;
using System.Drawing.Drawing2D;
using Hika.Config;

namespace Hika.Ui;

/// <summary>
/// Единая палитра и мелкие помощники рисования.
///
/// Тёмная намеренно: окно настроек открывают у ассистента, который живёт
/// в трее и показывается свечением по краям экрана. Светлое окно посреди
/// такого сценария выглядит вспышкой в лицо.
/// </summary>
public static class Theme
{
    public static readonly Color Background = Color.FromArgb(0x12, 0x15, 0x1A);
    public static readonly Color Panel = Color.FromArgb(0x17, 0x1B, 0x22);
    public static readonly Color Card = Color.FromArgb(0x1D, 0x22, 0x2B);
    public static readonly Color CardHover = Color.FromArgb(0x24, 0x2A, 0x36);
    public static readonly Color Border = Color.FromArgb(0x2A, 0x31, 0x3D);

    public static readonly Color Text = Color.FromArgb(0xE9, 0xEE, 0xF7);
    public static readonly Color TextDim = Color.FromArgb(0x8E, 0x99, 0xAC);
    public static readonly Color TextFaint = Color.FromArgb(0x5E, 0x68, 0x78);

    public static readonly Color Good = Color.FromArgb(0x3B, 0xE0, 0x7E);
    public static readonly Color Danger = Color.FromArgb(0xFF, 0x5A, 0x4E);

    /// <summary>Работает, но не так, как задумано, — жёлтый между зелёным и красным.</summary>
    public static readonly Color Warn = Color.FromArgb(0xFF, 0xC2, 0x4B);

    // Значения по умолчанию — до того, как станет известна выбранная личность.
    public static Color Accent { get; private set; } = Color.FromArgb(0x3B, 0x9B, 0xFF);
    public static Color AccentDim { get; private set; } = Color.FromArgb(0x14, 0x32, 0x4F);

    public static Persona Current { get; private set; } = Personas.Hika;

    public static event Action? AccentChanged;

    public static void ApplyPersona(string? personaId)
    {
        var persona = Personas.ById(personaId);
        if (persona.Id == Current.Id) return;

        Current = persona;
        Accent = FromHex(persona.Accent, Accent);
        AccentDim = FromHex(persona.AccentDim, AccentDim);

        AccentChanged?.Invoke();
    }

    public static Color AccentOf(Persona persona) => FromHex(persona.Accent, Color.DodgerBlue);
    public static Color AccentDimOf(Persona persona) => FromHex(persona.AccentDim, Color.DarkSlateBlue);

    public static Color FromHex(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;

        var text = hex.Trim().TrimStart('#');
        try
        {
            if (text.Length == 6)
                return Color.FromArgb(
                    Convert.ToInt32(text[..2], 16),
                    Convert.ToInt32(text.Substring(2, 2), 16),
                    Convert.ToInt32(text.Substring(4, 2), 16));
        }
        catch { /* мусор в настройках не повод падать */ }

        return fallback;
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Смешивает цвет с другим: 0 — первый, 1 — второй.</summary>
    public static Color Blend(Color a, Color b, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * amount),
            (int)(a.G + (b.G - a.G) * amount),
            (int)(a.B + (b.B - a.B) * amount));
    }

    // ---- Шрифты ----------------------------------------------------------

    private static string PickFamily()
    {
        // Segoe UI Variable — системный шрифт Windows 11, на десятке его нет.
        foreach (var name in new[] { "Segoe UI Variable Text", "Segoe UI", "Tahoma" })
        {
            try
            {
                using var probe = new FontFamily(name);
                return name;
            }
            catch { /* пробуем следующий */ }
        }

        return FontFamily.GenericSansSerif.Name;
    }

    private static readonly string Family = PickFamily();

    public static readonly Font Body = new(Family, 9.5f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font BodyBold = new(Family, 9.5f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font Small = new(Family, 8.25f, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font Section = new(Family, 12f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font Title = new(Family, 15f, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font Mono = new("Consolas", 9f, FontStyle.Regular, GraphicsUnit.Point);

    // ---- Рисование --------------------------------------------------------

    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();

        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }

    public static void FillRounded(Graphics g, Rectangle bounds, int radius, Color color)
    {
        using var path = RoundedRect(bounds, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void DrawRounded(Graphics g, Rectangle bounds, int radius, Color color, float width = 1f)
    {
        var inset = Rectangle.Inflate(bounds, -(int)Math.Ceiling(width / 2), -(int)Math.Ceiling(width / 2));
        using var path = RoundedRect(inset, radius);
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }
}
