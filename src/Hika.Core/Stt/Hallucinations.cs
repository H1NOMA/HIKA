using System.Text.RegularExpressions;

namespace Hika.Stt;

/// <summary>
/// Отсев выдуманного текста.
///
/// Whisper обучался в том числе на субтитрах с YouTube и на тишине уверенно
/// выдаёт куски, которых никто не произносил. У русской модели это особенно
/// заметно: «Субтитры сделал DimaTorzok», «Продолжение следует...»,
/// «Спасибо за просмотр!» — устойчивые галлюцинации, знакомые каждому,
/// кто прогонял через модель запись из тихой комнаты.
///
/// Без этого фильтра ассистент оживал бы от кашля и хлопнувшей двери,
/// так что список тут не косметика, а необходимая часть.
/// </summary>
public static partial class Hallucinations
{
    private static readonly string[] ExactPhrases =
    {
        // Русские — субтитровочные хвосты
        "субтитры сделал dimatorzok",
        "субтитры создавал dimatorzok",
        "субтитры делал dimatorzok",
        "редактор субтитров а.синецкая",
        "редактор субтитров м.лосева",
        "корректор а.егорова",
        "продолжение следует",
        "продолжение следует...",
        "спасибо за просмотр",
        "спасибо за просмотр!",
        "спасибо за внимание",
        "подписывайтесь на канал",
        "ставьте лайки и подписывайтесь",
        "не забудьте подписаться",
        "всем пока",
        "до новых встреч",
        "и ещё раз спасибо",

        // Английские
        "thank you",
        "thank you.",
        "thanks for watching",
        "thanks for watching!",
        "thank you for watching",
        "please subscribe",
        "subscribe to my channel",
        "you",
        "bye",
        "bye.",
        "so",
        "okay",
        "oh",
        "the",
        "yeah",
        "мм",
        "ага",
        "угу",
        "э",
        "а",
        "ну",
    };

    /// <summary>Пометки вроде [музыка], (шум ветра), *смех* — это не речь.</summary>
    [GeneratedRegex(@"[\[\(\*][^\]\)\*]{0,60}[\]\)\*]", RegexOptions.Compiled)]
    private static partial Regex AnnotationRegex();

    /// <summary>Знаки препинания и невидимые символы по краям.</summary>
    [GeneratedRegex(@"^[\s\p{P}\p{S}]+|[\s\p{P}\p{S}]+$", RegexOptions.Compiled)]
    private static partial Regex EdgeJunkRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    /// <summary>Убирает служебные пометки и лишние пробелы, оставляя саму речь.</summary>
    public static string Clean(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var cleaned = AnnotationRegex().Replace(text, " ");
        cleaned = WhitespaceRegex().Replace(cleaned, " ").Trim();
        return cleaned;
    }

    public static bool IsLikelyHallucination(string text)
    {
        var cleaned = Clean(text);
        if (cleaned.Length == 0) return true;

        var probe = EdgeJunkRegex().Replace(cleaned.ToLowerInvariant(), "");
        if (probe.Length == 0) return true;

        foreach (var phrase in ExactPhrases)
        {
            if (probe == phrase) return true;
        }

        // Хвост субтитров может приехать вместе с полезным текстом.
        if (probe.Contains("dimatorzok")) return true;

        // По основе, а не по словоформе: в выдаче встречается и «субтитры»,
        // и «субтитров», и «субтитрами».
        if (probe.Contains("субтитр") && (probe.Contains("редактор") || probe.Contains("корректор")))
            return true;

        // Одна повторяющаяся буква или слог: «ааааа», «та-та-та».
        if (IsMonotonous(probe)) return true;

        return false;
    }

    private static bool IsMonotonous(string s)
    {
        var letters = s.Where(char.IsLetter).ToArray();
        if (letters.Length == 0) return true;
        if (letters.Length < 3) return false;

        var distinct = letters.Distinct().Count();
        return distinct <= 1;
    }
}
