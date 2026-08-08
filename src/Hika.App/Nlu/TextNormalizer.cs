using System.Text;

namespace Hika.Nlu;

/// <summary>Приведение распознанного текста к виду, пригодному для сравнения.</summary>
public static class TextNormalizer
{
    /// <summary>
    /// Латинские буквы, неотличимые на вид от кириллических. Whisper временами
    /// смешивает алфавиты внутри одного слова, и без этой замены «сhrome»
    /// с кириллической «с» в начале не совпадёт ни с чем.
    /// </summary>
    private static readonly Dictionary<char, char> LatinLookalikes = new()
    {
        ['a'] = 'а', ['e'] = 'е', ['o'] = 'о', ['p'] = 'р', ['c'] = 'с',
        ['x'] = 'х', ['y'] = 'у', ['k'] = 'к', ['m'] = 'м', ['h'] = 'н',
        ['t'] = 'т', ['b'] = 'в', ['n'] = 'п',
    };

    /// <summary>Нижний регистр, «ё» к «е», знаки препинания к пробелам, схлопывание пробелов.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var sb = new StringBuilder(text.Length);
        var lastWasSpace = true;

        foreach (var raw in text)
        {
            var ch = char.ToLowerInvariant(raw);

            if (ch == 'ё') ch = 'е';
            else if (ch == 'й') ch = 'й';

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                sb.Append(' ');
                lastWasSpace = true;
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Если слово почти целиком кириллическое, но пара букв латинские —
    /// чинит их. Слова целиком на латинице не трогает.
    /// </summary>
    public static string FixMixedAlphabet(string word)
    {
        if (word.Length < 2) return word;

        int cyrillic = 0, latin = 0;
        foreach (var ch in word)
        {
            if (ch >= 'а' && ch <= 'я') cyrillic++;
            else if (ch >= 'a' && ch <= 'z') latin++;
        }

        if (cyrillic == 0 || latin == 0) return word;
        if (latin > cyrillic) return word;

        var sb = new StringBuilder(word.Length);
        foreach (var ch in word)
        {
            sb.Append(LatinLookalikes.TryGetValue(ch, out var fixedCh) ? fixedCh : ch);
        }
        return sb.ToString();
    }

    public static string[] Tokenize(string? text)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0) return Array.Empty<string>();

        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++) parts[i] = FixMixedAlphabet(parts[i]);
        return parts;
    }
}
