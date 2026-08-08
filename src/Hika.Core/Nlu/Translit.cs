using System.Text;

namespace Hika.Nlu;

/// <summary>
/// Сводит кириллицу и латиницу к общему звуковому написанию.
///
/// Это несущая конструкция всего распознавания команд. Человек говорит «фотошоп»,
/// а программа называется Photoshop; говорит «ворд» — а исполняемый файл winword.
/// Заранее перечислить все такие пары невозможно, зато можно свернуть оба
/// написания к одной строке и сравнить уже её.
///
/// Работает это заметно лучше, чем кажется:
///
///   фотошоп -> fotoshop     photoshop -> fotoshop     совпало точно
///   ворд    -> vord         word      -> vord         совпало точно
///   эксель  -> eksel        excel     -> eksel        совпало точно
///   твич    -> tvich        twitch    -> tvich        совпало точно
///   хром    -> hrom         chrome    -> chrom        разница в один символ
///   ютуб    -> iutub        youtube   -> ioutub       разница в один символ
///
/// Побочная выгода: когда Whisper работает в русском режиме и выдаёт английские
/// названия кириллицей («опен ворд»), команда всё равно доходит по назначению.
/// </summary>
public static class Translit
{
    private static readonly Dictionary<char, string> CyrToLat = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d",
        ['е'] = "e", ['ё'] = "e", ['ж'] = "zh", ['з'] = "z", ['и'] = "i",
        ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n",
        ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t",
        ['у'] = "u", ['ф'] = "f", ['х'] = "h", ['ц'] = "c", ['ч'] = "ch",
        ['ш'] = "sh", ['щ'] = "sh", ['ъ'] = "", ['ы'] = "y", ['ь'] = "",
        ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
    };

    public static string ToLatin(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";

        var sb = new StringBuilder(s.Length + 4);
        foreach (var ch in s.ToLowerInvariant())
        {
            if (CyrToLat.TryGetValue(ch, out var mapped)) sb.Append(mapped);
            else if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Свёртка латиницы к упрощённому звуковому виду. Порядок правил важен:
    /// многобуквенные сочетания разбираются раньше одиночных букв.
    /// </summary>
    public static string Fold(string latin)
    {
        if (string.IsNullOrEmpty(latin)) return "";

        var s = latin.ToLowerInvariant();
        var sb = new StringBuilder(s.Length);

        for (int i = 0; i < s.Length; i++)
        {
            var ch = s[i];

            if (!char.IsLetterOrDigit(ch)) continue;

            // Сочетания из четырёх и трёх букв
            if (i + 3 < s.Length && s[i] == 's' && s[i + 1] == 'h' && s[i + 2] == 'c' && s[i + 3] == 'h')
            {
                sb.Append("sh"); i += 3; continue;
            }
            if (i + 2 < s.Length)
            {
                var tri = s.Substring(i, 3);
                if (tri == "sch") { sb.Append("sh"); i += 2; continue; }
                if (tri == "tch") { sb.Append("ch"); i += 2; continue; }
            }

            // Сочетания из двух букв
            if (i + 1 < s.Length)
            {
                var pair = s.Substring(i, 2);
                switch (pair)
                {
                    case "ph": sb.Append('f'); i++; continue;
                    case "ck": sb.Append('k'); i++; continue;
                    case "qu": sb.Append("kv"); i++; continue;

                    // «ch» оставляем как есть и обязательно ловим здесь, до
                    // одиночной «c». Иначе она успеет превратиться в «k»,
                    // и русское «ч» (которое транслитерируется как раз в «ch»)
                    // разойдётся с английским: «твич» дало бы tvikh,
                    // а «twitch» — tvich.
                    case "ch": sb.Append("ch"); i++; continue;

                    // Английские гласные сочетания, которые по-русски звучат
                    // одним звуком. Именно так их и произносят:
                    //   steam     -> стим       ea -> и
                    //   speedtest -> спидтест   ee -> и
                    //   google    -> гугл       oo -> у
                    // Без этих трёх правил перечисленное не находилось бы
                    // ничем, кроме заранее прописанного синонима.
                    case "ea": sb.Append('i'); i++; continue;
                    case "ee": sb.Append('i'); i++; continue;
                    case "oo": sb.Append('u'); i++; continue;
                }
            }

            switch (ch)
            {
                case 'x': sb.Append("ks"); continue;
                case 'w': sb.Append('v'); continue;
                case 'q': sb.Append('k'); continue;
                case 'y': sb.Append('i'); continue;

                case 'c':
                    // Перед e, i, y латинская «c» звучит как «с», в остальных случаях как «к».
                    var next = i + 1 < s.Length ? s[i + 1] : '\0';
                    sb.Append(next is 'e' or 'i' or 'y' ? 's' : 'k');
                    continue;

                default:
                    sb.Append(ch);
                    continue;
            }
        }

        var folded = CollapseDoubles(sb.ToString());

        // Немая «e» на конце: chrome -> chrom, google -> googl.
        if (folded.Length > 3 && folded[^1] == 'e') folded = folded[..^1];

        return folded;
    }

    private static string CollapseDoubles(string s)
    {
        if (s.Length < 2) return s;

        var sb = new StringBuilder(s.Length);
        sb.Append(s[0]);
        for (int i = 1; i < s.Length; i++)
        {
            if (s[i] != s[i - 1]) sb.Append(s[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Числительные словами и их цифровая запись.
    ///
    /// Названия программ и игр сплошь и рядом заканчиваются цифрой —
    /// Helldivers 2, Diablo 4, Portal 2, — а произносят их словом. Без этой
    /// таблицы «запусти халдайверс два» не найдёт «Helldivers 2» никогда:
    /// «два» и «2» не похожи ни одной буквой.
    /// </summary>
    private static readonly Dictionary<string, string> NumberWords = new(StringComparer.Ordinal)
    {
        ["ноль"] = "0", ["один"] = "1", ["одна"] = "1", ["первый"] = "1",
        ["два"] = "2", ["две"] = "2", ["второй"] = "2", ["двойка"] = "2",
        ["три"] = "3", ["третий"] = "3", ["тройка"] = "3",
        ["четыре"] = "4", ["четвертый"] = "4", ["четвёртый"] = "4",
        ["пять"] = "5", ["пятый"] = "5",
        ["шесть"] = "6", ["шестой"] = "6",
        ["семь"] = "7", ["седьмой"] = "7",
        ["восемь"] = "8", ["восьмой"] = "8",
        ["девять"] = "9", ["девятый"] = "9",
        ["десять"] = "10", ["десятый"] = "10",
        ["одиннадцать"] = "11", ["двенадцать"] = "12",

        ["zero"] = "0", ["one"] = "1", ["two"] = "2", ["three"] = "3",
        ["four"] = "4", ["five"] = "5", ["six"] = "6", ["seven"] = "7",
        ["eight"] = "8", ["nine"] = "9", ["ten"] = "10",

        // Римские — их пишут в названиях не реже арабских.
        ["ii"] = "2", ["iii"] = "3", ["iv"] = "4", ["vi"] = "6",
    };

    /// <summary>Цифровая запись числительного или null, если слово не числительное.</summary>
    public static string? AsDigits(string word)
        => NumberWords.TryGetValue(word, out var digits) ? digits : null;

    /// <summary>
    /// Набор написаний, по которым имеет смысл сравнивать слово: само слово,
    /// его транслитерация и свёрнутая форма. Совпадение по любому из них считается совпадением.
    /// </summary>
    public static string[] Keys(string word)
    {
        var normalized = TextNormalizer.Normalize(word);
        if (normalized.Length == 0) return Array.Empty<string>();

        // Числительное сравнивается и словом, и цифрой: «два» должно найти «2».
        var digits = AsDigits(normalized);
        if (digits is not null) return new[] { normalized, digits };

        var latin = ToLatin(normalized);
        var folded = Fold(latin);

        // Для чисто латинских слов ToLatin вернёт то же самое — дубликаты убираем.
        if (folded == latin && latin == normalized) return new[] { normalized };
        if (folded == latin) return new[] { normalized, latin };
        if (latin == normalized) return new[] { normalized, folded };

        return new[] { normalized, latin, folded };
    }
}
