using Hika.Nlu;

namespace Hika.Speech;

/// <summary>
/// Диктовка: всё сказанное уходит в активное окно как текст.
///
/// Странно иметь распознавание речи и не уметь этого. Голосом человек
/// надиктовывает сообщение втрое быстрее, чем печатает его на клавиатуре,
/// и именно за этим к речи чаще всего и приходят — а команда «напечатай»
/// умеет ровно одну фразу и требует называть имя перед каждой.
///
/// Здесь два правила, и оба про то, как выйти. Диктовка, из которой нельзя
/// выбраться, — это программа, печатающая ваш разговор с домашними в чужую
/// переписку, и цена такой ошибки выше любой пользы от самой возможности.
/// Поэтому выход есть словом, есть по клавише, есть по молчанию и есть
/// выключением микрофона.
/// </summary>
public static class Dictation
{
    /// <summary>
    /// Слова, которыми диктовку заканчивают.
    ///
    /// Проверяются только на короткой фразе целиком. Иначе продиктованное
    /// «я сказал ему хватит» оборвало бы диктовку на полуслове — а это
    /// ровно та ошибка, после которой возможностью перестают пользоваться.
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "стоп", "хватит", "достаточно", "всё", "все", "конец", "закончили",
        "отбой", "закончить", "заканчивай", "закончи", "закончим", "прекрати",
        "останови", "остановись", "стой",
        "stop", "enough", "done",
    };

    /// <summary>Слова, которые в стоп-фразе ничего не добавляют.</summary>
    private static readonly HashSet<string> StopExtras = new(StringComparer.Ordinal)
    {
        "диктовка", "диктовку", "диктовки", "диктовать", "печатать", "писать",
        "печать", "набор", "набирать", "это", "уже", "давай", "так",
    };

    /// <summary>Сказанное означает «хватит диктовать».</summary>
    public static bool IsStop(string text)
    {
        var tokens = TextNormalizer.Tokenize(text);

        // Длинная фраза — это продиктованный текст, а не команда выйти.
        if (tokens.Length is 0 or > 3) return false;

        // Сначала спрашиваем общий разбор команд: там уже перечислены все
        // формулировки «закончи диктовку», «останови диктовку», «прекрати
        // печатать». Два списка, живущие порознь, неизбежно разъезжаются —
        // и разъехались: «закончи диктовку» окно настроек обещало, тесты
        // закрепляли, а диктовка честно печатала это в текст.
        if (CommandParser.Parse(text).Kind == IntentKind.DictationStop) return true;

        var hasStop = false;

        foreach (var token in tokens)
        {
            if (StopWords.Contains(token)) { hasStop = true; continue; }
            if (StopExtras.Contains(token)) continue;

            return false;
        }

        return hasStop;
    }

    /// <summary>
    /// Знаки препинания, названные вслух.
    ///
    /// Двусловные идут первыми: «вопросительный знак» должен разобраться
    /// целиком, а не превратиться в слово «вопросительный» и знак.
    /// </summary>
    private static readonly (string[] Words, string Mark)[] Marks =
    {
        (new[] { "вопросительный", "знак" }, "?"),
        (new[] { "восклицательный", "знак" }, "!"),
        (new[] { "знак", "вопроса" }, "?"),
        (new[] { "новая", "строка" }, "\n"),
        (new[] { "новую", "строку" }, "\n"),
        (new[] { "с", "новой", "строки" }, "\n"),
        (new[] { "перенос", "строки" }, "\n"),
        (new[] { "новый", "абзац" }, "\n\n"),
        (new[] { "красная", "строка" }, "\n\n"),
        (new[] { "открыть", "скобку" }, "("),
        (new[] { "закрыть", "скобку" }, ")"),
        (new[] { "открыть", "кавычки" }, "«"),
        (new[] { "закрыть", "кавычки" }, "»"),
        (new[] { "многоточие" }, "…"),
        (new[] { "двоеточие" }, ":"),
        (new[] { "точка", "с", "запятой" }, ";"),
        (new[] { "точка" }, "."),
        (new[] { "точку" }, "."),
        (new[] { "запятая" }, ","),
        (new[] { "запятую" }, ","),
        (new[] { "тире" }, " —"),
        (new[] { "дефис" }, "-"),
        (new[] { "абзац" }, "\n\n"),
        (new[] { "скобка" }, "("),
        (new[] { "кавычки" }, "«"),
    };

    /// <summary>Знаки, к которым нельзя приписать ещё один такой же.</summary>
    private const string Trailing = ".,!?;:—…-«» ";

    /// <summary>После этого следующее слово начинает предложение.</summary>
    private const string Sentence = ".!?…\n";

    /// <summary>
    /// Готовит распознанное к набору: превращает названные вслух знаки
    /// в сами знаки и приводит текст в человеческий вид.
    ///
    /// Двусмысленность здесь неизбежна и признаётся честно: продиктованное
    /// «поставь точку» превратится в «поставь .». Так устроены все системы
    /// диктовки без исключения, и лечится это только тем, что человек
    /// об этом знает.
    /// </summary>
    /// <param name="startsSentence">
    /// Начинается ли с этой фразы новое предложение. Важно потому, что
    /// диктовка идёт кусками: человек говорит «я пошёл в магазин», молчит,
    /// говорит «и купил хлеба». Распознавание видит два отдельных куска
    /// и каждый начинает с заглавной — а это одно предложение.
    /// </param>
    public static string Punctuate(string text, bool startsSentence = true)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var result = new System.Text.StringBuilder(text.Length + 8);

        var capitalizeNext = startsSentence;

        for (int i = 0; i < words.Length; i++)
        {
            var mark = MatchMark(words, i, out var taken);

            if (mark is not null)
            {
                i += taken - 1;

                // Whisper уже поставил свою точку в конце фразы, а человек
                // назвал знак вслух. Два знака подряд — верный признак того,
                // что один из них лишний, и лишний всегда прежний.
                while (result.Length > 0 && Trailing.Contains(result[^1])) result.Length--;

                result.Append(mark);

                capitalizeNext = Sentence.Contains(mark[^1]);
                continue;
            }

            var word = Clean(words[i]);
            if (word.Length == 0) continue;

            if (result.Length > 0 && result[^1] != '\n') result.Append(' ');

            if (capitalizeNext && char.IsLower(word[0]))
            {
                result.Append(char.ToUpperInvariant(word[0])).Append(word.AsSpan(1));
            }
            else if (!capitalizeNext && result.Length == 0 && char.IsUpper(word[0]) && !AllCaps(word))
            {
                // Предложение продолжается, а распознавание всё равно начало
                // кусок с заглавной — оно не знает, что было сказано минуту
                // назад. Аббревиатуры при этом не трогаем.
                result.Append(char.ToLowerInvariant(word[0])).Append(word.AsSpan(1));
            }
            else
            {
                result.Append(word);
            }

            capitalizeNext = word.Length > 0 && Sentence.Contains(word[^1]);
        }

        return result.ToString();
    }

    /// <summary>Знак, начинающийся с этого слова. Null — обычное слово.</summary>
    private static string? MatchMark(string[] words, int index, out int taken)
    {
        foreach (var (needle, mark) in Marks)
        {
            if (index + needle.Length > words.Length) continue;

            var ok = true;
            for (int i = 0; i < needle.Length; i++)
            {
                if (!string.Equals(Bare(words[index + i]), needle[i], StringComparison.OrdinalIgnoreCase))
                {
                    ok = false;
                    break;
                }
            }

            if (!ok) continue;

            taken = needle.Length;
            return mark;
        }

        taken = 0;
        return null;
    }

    /// <summary>Слово без знаков препинания вокруг — для сравнения со списком.</summary>
    private static string Bare(string word) => word.Trim('.', ',', '!', '?', ';', ':', '"', '«', '»', '(', ')', '-', '—');

    /// <summary>Слово так, как оно пойдёт в текст.</summary>
    private static string Clean(string word) => word.Trim();

    /// <summary>Слово написано целиком заглавными — аббревиатура, её не трогаем.</summary>
    private static bool AllCaps(string word)
    {
        var letters = 0;

        foreach (var ch in word)
        {
            if (!char.IsLetter(ch)) continue;
            if (char.IsLower(ch)) return false;
            letters++;
        }

        return letters > 1;
    }

    /// <summary>Кончилось ли набранное концом предложения.</summary>
    public static bool EndsSentence(string typed)
    {
        for (int i = typed.Length - 1; i >= 0; i--)
        {
            var ch = typed[i];
            if (char.IsWhiteSpace(ch) && ch != '\n') continue;

            return Sentence.Contains(ch);
        }

        // Пустое означает, что ничего ещё не набрано, — то есть начало.
        return true;
    }
}
