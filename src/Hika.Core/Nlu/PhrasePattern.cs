namespace Hika.Nlu;

/// <summary>
/// Команда, описанная не фразой, а её устройством.
///
/// Заготовленные фразы плохи тем, что их надо перечислять. «Включи музыку»,
/// «поставь музыку», «запусти музыку», «врубай музон», «давай мою музыку» —
/// пять записей ради одной мысли, и шестую человек всё равно скажет мимо
/// списка. Дальше список растёт, начинает противоречить сам себе, и однажды
/// «включи стим» совпадает с «включи музыку».
///
/// Здесь команда описывается по слотам: место для глагола, место для
/// уточнения, место для предмета. У каждого слота — свой набор слов, любой
/// может быть необязательным. Пять записей превращаются в одну строку,
/// а покрытие получается шире перечисления: работают и те сочетания,
/// которые никто не выписывал.
///
/// Сопоставление нечёткое и по звучанию — теми же правилами, что и весь
/// остальной разбор, — поэтому «врубай музон» находится наравне
/// с «включи музыку».
/// </summary>
public sealed class PhrasePattern
{
    public IntentKind Kind { get; }

    /// <summary>Слоты по порядку. Каждый занимает не больше одного слова.</summary>
    public IReadOnlyList<Slot> Slots { get; }

    /// <summary>
    /// Минимальное совпадение. Отдельно у каждой команды: там, где ошибка
    /// дешёвая, можно прощать больше.
    /// </summary>
    public double Threshold { get; }

    private PhrasePattern(IntentKind kind, IReadOnlyList<Slot> slots, double threshold)
    {
        Kind = kind;
        Slots = slots;
        Threshold = threshold;
    }

    /// <summary>
    /// Собирает шаблон. Слот, начинающийся с «?», необязателен.
    /// Внутри слота варианты перечисляются через «|».
    /// </summary>
    /// <example>
    /// Of(IntentKind.MediaNext, 0.82, "?включи|переключи|давай", "следующий|следующая|дальше", "?трек|песню")
    /// </example>
    public static PhrasePattern Of(IntentKind kind, double threshold, params string[] slots)
    {
        var built = new List<Slot>(slots.Length);

        foreach (var raw in slots)
        {
            var optional = raw.StartsWith('?');
            var body = optional ? raw[1..] : raw;

            var words = body
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(TextNormalizer.Normalize)
                .Where(w => w.Length > 0)
                .ToArray();

            if (words.Length == 0) continue;

            built.Add(new Slot(words.Select(Translit.Keys).ToArray(), optional));
        }

        return new PhrasePattern(kind, built, threshold);
    }

    /// <summary>Один слот: набор допустимых слов и признак необязательности.</summary>
    public sealed class Slot
    {
        public string[][] Keys { get; }
        public bool Optional { get; }

        public Slot(string[][] keys, bool optional)
        {
            Keys = keys;
            Optional = optional;
        }

        /// <summary>Насколько слово подходит этому слоту.</summary>
        public double Similarity(string[] tokenKeys)
        {
            double best = 0;
            foreach (var variant in Keys)
            {
                var score = FuzzyMatch.BestSimilarity(tokenKeys, variant);
                if (score > best) best = score;
            }
            return best;
        }
    }

    /// <summary>
    /// Насколько фраза похожа на этот шаблон, 0..1. Ноль — не подходит вовсе.
    ///
    /// Требуется, чтобы разошлись все слова: фраза «включи музыку в стиме»
    /// не должна считаться просьбой включить музыку только потому, что первые
    /// два слова совпали. Лишнее слово означает другую команду.
    /// </summary>
    public double Match(string[][] spokenKeys)
    {
        var n = spokenKeys.Length;
        var m = Slots.Count;

        if (n == 0 || n > m) return 0;

        // Разбор идёт динамическим программированием по решётке
        // «слов разобрано × слотов пройдено». Жадный проход здесь ошибается:
        // необязательный слот, съевший слово, может лишить его обязательный.
        const double Impossible = double.NegativeInfinity;

        var best = new double[n + 1, m + 1];
        for (int i = 0; i <= n; i++)
        {
            for (int j = 0; j <= m; j++) best[i, j] = Impossible;
        }
        best[0, 0] = 0;

        for (int j = 0; j < m; j++)
        {
            var slot = Slots[j];

            for (int i = 0; i <= n; i++)
            {
                if (double.IsNegativeInfinity(best[i, j])) continue;

                // Слот пропущен — так можно только с необязательным.
                if (slot.Optional && best[i, j] > best[i, j + 1]) best[i, j + 1] = best[i, j];

                if (i >= n) continue;

                var similarity = slot.Similarity(spokenKeys[i]);
                if (similarity <= 0.35) continue;   // совсем не то слово

                var total = best[i, j] + similarity;
                if (total > best[i + 1, j + 1]) best[i + 1, j + 1] = total;
            }
        }

        var result = best[n, m];
        return double.IsNegativeInfinity(result) ? 0 : result / n;
    }
}
