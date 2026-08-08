using System.Drawing;
using System.Globalization;
using Hika.Config;

namespace Hika.Overlay;

public enum OverlayState
{
    /// <summary>Ничего не показываем. Окна скрыты, ресурсы не тратятся.</summary>
    Hidden,

    /// <summary>Услышали чей-то голос, но ещё не знаем, к нам ли обращаются. Едва заметное свечение.</summary>
    Sensing,

    /// <summary>Имя прозвучало, слушаем команду. Кайма живёт в такт голосу.</summary>
    Listening,

    /// <summary>Распознаём и выполняем. Спокойная бегущая волна.</summary>
    Thinking,

    /// <summary>Команда выполнена. Короткая вспышка и затухание.</summary>
    Success,

    /// <summary>Не поняли или не смогли. Короткая вспышка другого цвета.</summary>
    Failed,
}

/// <summary>Сторона экрана. Порядок совпадает с порядком цветов в настройках.</summary>
public enum Edge { Top = 0, Right = 1, Bottom = 2, Left = 3 }

/// <summary>Цвета для каждого состояния.</summary>
public sealed class GlowPalette
{
    /// <summary>Цвет каждой стороны в обычном состоянии.</summary>
    public Color[] Edges { get; }

    public Color Success { get; }
    public Color Failed { get; }

    public GlowPalette(OverlayConfig config, string? personaId = null)
    {
        // Цвета личности по умолчанию: свечение должно совпадать со значком
        // возле часов, иначе связь между ними приходится держать в голове.
        var colors = config.UsePersonaColors
            ? Personas.ById(personaId).GlowColors.ToList()
            : config.Colors ?? new List<string>();

        Edges = new Color[4];
        for (int i = 0; i < 4; i++)
        {
            Edges[i] = i < colors.Count
                ? Parse(colors[i], DefaultEdge(i))
                : DefaultEdge(i);
        }

        Success = Parse(config.SuccessColor, Color.FromArgb(59, 224, 126));
        Failed = Parse(config.ErrorColor, Color.FromArgb(255, 90, 78));
    }

    private static Color DefaultEdge(int index) => index switch
    {
        0 => Color.FromArgb(58, 160, 255),
        1 => Color.FromArgb(138, 108, 255),
        2 => Color.FromArgb(255, 95, 162),
        3 => Color.FromArgb(49, 214, 188),
        _ => Color.White,
    };

    /// <summary>Цвета для конкретного состояния: успех и ошибка перекрашивают всю кайму разом.</summary>
    public Color ColorFor(OverlayState state, Edge edge) => state switch
    {
        OverlayState.Success => Success,
        OverlayState.Failed => Failed,
        _ => Edges[(int)edge],
    };

    public static Color Parse(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;

        var text = hex.Trim().TrimStart('#');

        try
        {
            if (text.Length == 3)
            {
                // Короткая запись: #abc
                var r3 = Convert.ToInt32(new string(text[0], 2), 16);
                var g3 = Convert.ToInt32(new string(text[1], 2), 16);
                var b3 = Convert.ToInt32(new string(text[2], 2), 16);
                return Color.FromArgb(r3, g3, b3);
            }

            if (text.Length is 6 or 8)
            {
                // Альфу из записи игнорируем: прозрачностью управляет состояние, а не цвет.
                var offset = text.Length == 8 ? 2 : 0;
                var r = int.Parse(text.Substring(offset, 2), NumberStyles.HexNumber);
                var g = int.Parse(text.Substring(offset + 2, 2), NumberStyles.HexNumber);
                var b = int.Parse(text.Substring(offset + 4, 2), NumberStyles.HexNumber);
                return Color.FromArgb(r, g, b);
            }
        }
        catch
        {
            // Неразборчивый цвет — не повод падать.
        }

        return fallback;
    }
}
