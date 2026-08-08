using Hika.Nlu;

namespace Hika.Learning;

/// <summary>
/// Правила, по которым профиль меняется от услышанного.
///
/// Вынесены отдельно и целиком без состояния — так их можно проверить тестами
/// до последнего порога. Обучение, которое нельзя проверить, отличить
/// от случайной порчи данных невозможно, а портит оно ровно то, ради чего
/// всё затевалось: узнавание команд.
/// </summary>
public static class Adaptation
{
    /// <summary>Слова короче этого не запоминаем: «в», «на», «и» ничего не подсказывают.</summary>
    private const int MinTermLength = 3;

    /// <summary>
    /// Служебные слова, которые звучат в каждой второй команде. В словарь
    /// распознавания им нельзя: они вытеснят оттуда названия программ,
    /// ради которых словарь и нужен.
    /// </summary>
    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "открой", "открыть", "запусти", "запустить", "включи", "включить",
        "покажи", "показать", "найди", "найти", "закрой", "закрыть",
        "сделай", "поставь", "давай", "можешь", "пожалуйста", "мне", "нам",
        "это", "вот", "там", "тут", "так", "уже", "ещё", "еще", "как", "что",
        "или", "если", "чтобы", "потом", "когда", "тоже", "нужно", "надо",
        "хочу", "буду", "было", "быть", "есть", "него", "неё", "нее", "они",
        "open", "close", "launch", "start", "please", "the", "and", "for",
        "you", "this", "that", "with", "from", "have", "just", "want",
    };

    /// <summary>
    /// Записывает услышанную фразу в профиль.
    /// </summary>
    /// <param name="useful">Команда из этой фразы действительно выполнилась.</param>
    public static void Observe(UserProfile profile, string text, bool useful)
    {
        profile.Utterances++;

        foreach (var token in TextNormalizer.Tokenize(text))
        {
            if (token.Length < MinTermLength) continue;
            if (Stop.Contains(token)) continue;
            if (token.All(char.IsDigit)) continue;

            if (!profile.Terms.TryGetValue(token, out var stat))
            {
                stat = new TermStat { Count = 0 };
                profile.Terms[token] = stat;
            }

            stat.Count++;
            stat.LastSeen = DateTime.UtcNow;
            if (useful) stat.Useful++;
        }
    }

    /// <summary>
    /// Слова для словаря распознавания — по убыванию полезности.
    ///
    /// Отбор устроен так, чтобы редкое, но точно значимое слово («халдайверс»,
    /// названное дважды и оба раза приведшее к запуску) било частое, но пустое
    /// («погода», сказанное десять раз ни к чему). Ради этого слово,
    /// подтверждённое делом, весит впятеро: подсказка нужна не тому, что человек
    /// произносит чаще, а тому, что он произносит осмысленно и что модель
    /// при этом коверкает.
    ///
    /// Всё старше двух месяцев постепенно уходит: программы ставят и удаляют,
    /// и словарь должен это переживать.
    /// </summary>
    public static List<string> PromptTerms(UserProfile profile, int max)
    {
        if (max <= 0 || profile.Terms.Count == 0) return new List<string>();

        var now = DateTime.UtcNow;

        return profile.Terms
            .Select(kv =>
            {
                var age = (now - kv.Value.LastSeen).TotalDays;
                var decay = age <= 60 ? 1.0 : Math.Max(0.15, 1.0 - (age - 60) / 120.0);
                var score = (kv.Value.Count + kv.Value.Useful * 5.0) * decay;
                return (Word: kv.Key, Score: score);
            })
            .Where(t => t.Score >= 1.0)
            .OrderByDescending(t => t.Score)
            .ThenBy(t => t.Word, StringComparer.Ordinal)
            .Take(max)
            .Select(t => t.Word)
            .ToList();
    }

    /// <summary>
    /// Запоминает, как распознавание расслышало имя, когда узнало его
    /// неуверенно.
    ///
    /// Смысл — в повторяемости. Один раз «фика» вместо «хика» — оговорка модели.
    /// Пять раз подряд — значит, человек так и произносит, и подстраиваться
    /// должна программа, а не человек.
    /// </summary>
    /// <param name="score">Насколько похоже на настоящее имя, 0..1.</param>
    /// <returns>Написание, набравшее нужное число повторов и готовое пойти в слова пробуждения.</returns>
    public static string? ObserveWakeVariant(UserProfile profile, string heard, double score, int threshold)
    {
        var word = TextNormalizer.Normalize(heard).Trim();
        if (word.Length is < 3 or > 12) return null;
        if (word.Contains(' ')) return null;

        // Совсем непохожее не запоминаем: иначе в имена уедет любое слово,
        // сказанное в тишине. Полное совпадение тоже незачем — оно и так работает.
        if (score is < 0.3 or >= 0.92) return null;

        var count = profile.WakeVariants.TryGetValue(word, out var n) ? n + 1 : 1;
        profile.WakeVariants[word] = count;

        return count == Math.Max(2, threshold) ? word : null;
    }

    /// <summary>Написания имени, набравшие достаточно повторов.</summary>
    public static List<string> ConfirmedWakeVariants(UserProfile profile, int threshold)
        => profile.WakeVariants
            .Where(kv => kv.Value >= Math.Max(2, threshold))
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToList();

    /// <summary>
    /// Насколько похожими должны быть неудачная и следующая за ней удачная
    /// команда, чтобы счесть вторую исправлением первой.
    ///
    /// Порог невысокий: «халдайверс два» и «хеллдайверс» — это одно и то же,
    /// хотя написано по-разному. Но и не нулевой, иначе после каждой неудачи
    /// в синонимы уедет первое, что человек сделал потом.
    /// </summary>
    public const double CorrectionSimilarity = 0.34;

    /// <summary>
    /// Учит синоним по паре «не вышло — вышло». Возвращает false, если пара
    /// непохожая и связывать их нельзя.
    /// </summary>
    public static bool LearnAlias(UserProfile profile, string failedPhrase, string succeededPhrase,
        string entryId, string entryName)
    {
        var key = TextNormalizer.Normalize(failedPhrase).Trim();
        if (key.Length < 3 || string.IsNullOrWhiteSpace(entryId)) return false;

        // Уже знаем — не трогаем, но отмечаем повтор.
        if (profile.Aliases.TryGetValue(key, out var existing))
        {
            if (existing.EntryId != entryId && existing.Manual) return false;
            existing.EntryId = entryId;
            existing.EntryName = entryName;
            existing.Count++;
            existing.LastSeen = DateTime.UtcNow;
            return true;
        }

        var similarity = FuzzyMatch.PhraseSimilarity(
            TextNormalizer.Tokenize(failedPhrase).Select(Translit.Keys).ToArray(),
            TextNormalizer.Tokenize(succeededPhrase).Select(Translit.Keys).ToArray());

        if (similarity < CorrectionSimilarity) return false;

        profile.Aliases[key] = new AliasStat
        {
            EntryId = entryId,
            EntryName = entryName,
            Count = 1,
            LastSeen = DateTime.UtcNow,
        };
        return true;
    }

    public static void RememberLaunch(UserProfile profile, string entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId)) return;
        profile.Launches[entryId] = profile.Launches.TryGetValue(entryId, out var n) ? n + 1 : 1;
        profile.Successes++;
    }

    /// <summary>
    /// Прибавка к оценке записи каталога за то, что её уже запускали.
    ///
    /// Нарочно маленькая и с потолком: она должна решать спор между двумя
    /// одинаково похожими названиями, а не перебивать сходство. Иначе один раз
    /// открытый «Steam Cleaner» начнёт перехватывать «стим» навсегда.
    /// </summary>
    public static double LaunchBoost(UserProfile profile, string entryId, double max)
    {
        if (max <= 0 || !profile.Launches.TryGetValue(entryId, out var count) || count <= 0) return 0;

        // Логарифм: первый запуск даёт заметную часть прибавки, сотый — почти ничего.
        return Math.Min(max, max * Math.Log(1 + count) / Math.Log(21));
    }

    /// <summary>Убирает из профиля всё, что давно не подтверждалось. Вызывается при сохранении.</summary>
    public static void Prune(UserProfile profile, int maxTerms = 4000)
    {
        if (profile.Terms.Count <= maxTerms) return;

        var doomed = profile.Terms
            .OrderBy(kv => kv.Value.Useful)
            .ThenBy(kv => kv.Value.Count)
            .ThenBy(kv => kv.Value.LastSeen)
            .Take(profile.Terms.Count - maxTerms * 3 / 4)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var word in doomed) profile.Terms.Remove(word);
    }
}
