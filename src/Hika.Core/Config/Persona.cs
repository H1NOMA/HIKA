namespace Hika.Config;

/// <summary>
/// Личность ассистента: имя, на которое он отзывается, и цвет, которым живёт.
///
/// Цвет здесь не украшение. Он один и тот же в значке возле часов, в свечении
/// по краям экрана и в окне настроек — по нему видно, кого ты позвал, боковым
/// зрением и не читая ни одной надписи.
/// </summary>
public sealed record Persona(
    string Id,
    string Name,
    IReadOnlyList<string> Words,
    string Accent,
    string AccentDim,
    IReadOnlyList<string> GlowColors)
{
    /// <summary>Главное имя — то, что показывается в интерфейсе.</summary>
    public string PrimaryWord => Words[0];
}

public static class Personas
{
    /// <summary>Синяя. Спокойный холодный свет.</summary>
    public static readonly Persona Hika = new(
        Id: "hika",
        Name: "Хика",
        Words: new[] { "хика", "хико" },
        Accent: "#3B9BFF",
        AccentDim: "#14324F",
        // По кругу: сверху синий, к правому краю уходит в фиолетовый,
        // снизу в бирюзу и обратно. Переход мягкий, без резких стыков в углах.
        GlowColors: new[] { "#3B9BFF", "#6C7CFF", "#31D6BC", "#3BBFFF" });

    /// <summary>Оранжевая. Тёплый свет, заметнее на тёмном экране.</summary>
    public static readonly Persona Avi = new(
        Id: "avi",
        Name: "Ави",
        Words: new[] { "ави" },
        Accent: "#FF8A3D",
        AccentDim: "#4A2A12",
        GlowColors: new[] { "#FF8A3D", "#FF5F6D", "#FFC24B", "#FF7A2E" });

    public static readonly IReadOnlyList<Persona> All = new[] { Hika, Avi };

    public static Persona ById(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Hika;

    public static Persona Other(Persona persona) => persona.Id == Hika.Id ? Avi : Hika;

    /// <summary>
    /// Список имён для выбранной личности. Главное идёт первым — по нему
    /// подписывается интерфейс, а остальные просто тоже срабатывают.
    /// </summary>
    public static List<string> WakeWordsFor(string personaId, bool respondToBoth)
    {
        var persona = ById(personaId);
        var words = new List<string>(persona.Words);

        if (respondToBoth) words.AddRange(Other(persona).Words);

        return words;
    }
}
