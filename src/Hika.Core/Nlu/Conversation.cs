namespace Hika.Nlu;

/// <summary>
/// Отличает вопрос от команды.
///
/// Разница дорогая. Команда исполняется за миллисекунды и не требует интернета;
/// вопрос уходит в языковую модель, стоит денег и секунды ожидания. Ошибиться
/// в любую сторону неприятно: «открой стим», ушедшее в разговор, — это
/// потерянная секунда и вежливый ответ вместо запущенной игры, а «расскажи
/// про стим», ушедшее в каталог, — запущенный Steam вместо ответа.
///
/// Поэтому признаков два уровня.
///
/// Явные — те, после которых команды не бывает вовсе: «расскажи», «объясни»,
/// «что такое», «почему». Такое уходит в разговор сразу, минуя каталог.
///
/// Слабые — вопросительные слова, которые встречаются и в командах:
/// «как», «где», «сколько». Они дают повод обратиться к разговору только после
/// того, как каталог ничего не нашёл. Так быстрый путь остаётся быстрым.
/// </summary>
public static class Conversation
{
    /// <summary>Начала фраз, после которых команды не бывает.</summary>
    private static readonly string[][] StrongOpeners =
    {
        new[] { "расскажи" }, new[] { "рассказывай" }, new[] { "расскажешь" },
        new[] { "объясни" }, new[] { "объясняй" }, new[] { "поясни" },
        new[] { "что", "такое" }, new[] { "кто", "такой" }, new[] { "кто", "такая" },
        new[] { "что", "значит" }, new[] { "что", "означает" },
        new[] { "почему" }, new[] { "зачем" }, new[] { "отчего" },
        new[] { "посоветуй" }, new[] { "подскажи" }, new[] { "помоги" },
        new[] { "придумай" }, new[] { "сочини" }, new[] { "напиши" },
        new[] { "переведи" }, new[] { "посчитай" }, new[] { "сравни" },
        new[] { "как", "думаешь" }, new[] { "как", "считаешь" }, new[] { "что", "думаешь" },
        new[] { "что", "скажешь" }, new[] { "твоё", "мнение" }, new[] { "твое", "мнение" },
        new[] { "как", "дела" }, new[] { "как", "ты" }, new[] { "как", "жизнь" },
        new[] { "давай", "поговорим" }, new[] { "поговорим" }, new[] { "поболтаем" },
        new[] { "правда", "ли" }, new[] { "стоит", "ли" }, new[] { "можно", "ли" },
        new[] { "в", "чем", "разница" }, new[] { "в", "чём", "разница" },

        new[] { "tell", "me" }, new[] { "explain" }, new[] { "what", "is" },
        new[] { "who", "is" }, new[] { "why" }, new[] { "how", "do" },
        new[] { "translate" }, new[] { "write" }, new[] { "summarize" },
    };

    /// <summary>Вопросительные слова, встречающиеся и в командах. Только как запасной признак.</summary>
    private static readonly HashSet<string> WeakOpeners = new(StringComparer.Ordinal)
    {
        "что", "кто", "где", "когда", "куда", "откуда", "сколько", "какой",
        "какая", "какое", "какие", "чей", "чем", "кем", "зачем", "как",
        "what", "who", "where", "when", "how", "which", "whose", "why",
    };

    /// <summary>
    /// Затравки, с которых начинается продолжение разговора: «а почему?»,
    /// «ну и что дальше», «слушай, а зачем».
    ///
    /// Без их снятия продолжение не опознаётся вовсе: вопросительное слово
    /// стоит вторым, а проверка смотрит на первое. А продолжение — это ровно
    /// то, ради чего разговор и заводится.
    /// </summary>
    private static readonly HashSet<string> LeadIns = new(StringComparer.Ordinal)
    {
        "а", "и", "но", "ну", "так", "вот", "ладно", "хорошо", "окей", "ок",
        "слушай", "скажи", "погоди", "стоп", "кстати", "тогда", "значит",
        "and", "but", "so", "ok", "okay", "well", "hey",
    };

    /// <summary>Слова, после которых это точно не разговор, чем бы фраза ни начиналась.</summary>
    private static readonly HashSet<string> HardCommandWords = new(StringComparer.Ordinal)
    {
        "открой", "открыть", "запусти", "запустить", "включи", "включить",
        "закрой", "закрыть", "сверни", "свернуть", "громче", "тише",
        "скриншот", "заблокируй", "пауза",
    };

    /// <summary>
    /// Фраза заведомо не команда — можно сразу в разговор, не тратя время
    /// на поиск по каталогу.
    /// </summary>
    public static bool IsDefinitelyTalk(string text)
    {
        var tokens = StripLeadIns(TextNormalizer.Tokenize(text));
        if (tokens.Length == 0) return false;

        // «Хико, расскажи анекдот и открой ютуб» — тут всё-таки есть команда,
        // и запускать её важнее, чем поговорить.
        if (tokens.Any(HardCommandWords.Contains)) return false;

        foreach (var opener in StrongOpeners)
        {
            if (StartsWith(tokens, opener)) return true;
        }

        return false;
    }

    /// <summary>
    /// Снимает затравки продолжения. Не больше двух и никогда до пустоты:
    /// «ну так что?» — вопрос, а «ну» и «так» по отдельности — ничего.
    /// </summary>
    private static string[] StripLeadIns(string[] tokens)
    {
        var start = 0;
        while (start < tokens.Length - 1 && start < 2 && LeadIns.Contains(tokens[start])) start++;
        return start == 0 ? tokens : tokens[start..];
    }

    /// <summary>
    /// Фраза может оказаться вопросом. Проверяется только после того,
    /// как исполнить её как команду не вышло.
    /// </summary>
    public static bool MightBeTalk(string text)
    {
        var tokens = StripLeadIns(TextNormalizer.Tokenize(text));
        if (tokens.Length == 0) return false;

        if (IsDefinitelyTalk(text)) return true;
        if (tokens.Any(HardCommandWords.Contains)) return false;

        // Знак вопроса распознавание ставит само, и это самый честный признак.
        if (text.Contains('?')) return true;

        // Вопросительное слово в начале — но одинокое слово вопросом не считаем:
        // «как» само по себе это не вопрос, а обрывок.
        if (tokens.Length >= 2 && WeakOpeners.Contains(tokens[0])) return true;

        // Длинная фраза без единого совпадения в каталоге — скорее речь,
        // чем название программы. Названий из семи слов не бывает.
        return tokens.Length >= 7;
    }

    private static bool StartsWith(string[] tokens, string[] prefix)
    {
        if (tokens.Length < prefix.Length) return false;
        for (int i = 0; i < prefix.Length; i++)
        {
            if (tokens[i] != prefix[i]) return false;
        }
        return true;
    }
}
