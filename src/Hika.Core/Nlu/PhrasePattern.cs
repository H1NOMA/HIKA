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

    /// <summary>Слово похоже на слот хотя бы отдалённо — ниже этого сравнивать нечего.</summary>
    private const double SlotFloor = 0.35;

    /// <summary>
    /// Насколько обязательный слот должен совпасть сам по себе.
    ///
    /// Оценка шаблона — среднее по слотам, и без этого правила точно
    /// совпавший глагол вытягивает совсем не тот предмет. Так «включи
    /// мозиллу» разбиралось как «включи музыку», «открой переводчик» —
    /// как «открой проводник», «включи заметки» — как «замедли»: единица
    /// за глагол и семь десятых за предмет дают в среднем достаточно,
    /// хотя предмет — другое слово.
    ///
    /// Требование ровно одно и берётся у самой команды: слово, на котором
    /// она держится, обязано быть похоже не меньше, чем требует её порог.
    /// Это не отдельное число, которое пришлось бы подбирать, а то же самое,
    /// которым команда уже описана: где ошибка дешёвая, порог мягче, и слову
    /// прощается больше.
    ///
    /// Необязательных слотов правило не касается: они на то и необязательные,
    /// что могут не совпасть вовсе.
    /// </summary>
    private double RequiredFloor => Threshold;

    /// <summary>
    /// Насколько фраза похожа на этот шаблон, 0..1. Ноль — не подходит вовсе.
    ///
    /// Два правила определяют здесь всё.
    ///
    /// Первое: слова-паразиты не существуют. «Открой ко мне настройки»
    /// и «открой настройки» — одна и та же просьба, и «ко мне» не должно
    /// ей мешать. Такие слова пропускаются в любом месте фразы и не влияют
    /// на оценку — ни в плюс, ни в минус.
    ///
    /// Второе: всё остальное обязано разойтись по слотам. «Включи музыку
    /// в стиме» — это Steam, а не музыка, и отличается от «включи музыку»
    /// ровно одним значащим словом. Стоит начать прощать лишние слова —
    /// и любая команда начнёт совпадать с любой.
    /// </summary>
    /// <param name="ignorable">
    /// Слова, которых для разбора не существует. Обычно предлоги, частицы
    /// и вежливость.
    /// </param>
    public double Match(string[][] spokenKeys, Func<int, bool>? ignorable = null)
        => Match(spokenKeys, out _, ignorable);

    /// <param name="matched">
    /// Сколько слов легло в слоты. Нужно вызывающему: команда, опознанная
    /// по одному-единственному слову, обязана совпасть точнее — иначе
    /// «твиттер» становится театральным режимом, а «скайп» — следующим треком.
    /// </param>
    public double Match(string[][] spokenKeys, out int matched, Func<int, bool>? ignorable = null)
    {
        matched = 0;

        var n = spokenKeys.Length;
        var m = Slots.Count;

        if (n == 0) return 0;

        // Разбор идёт динамическим программированием по решётке
        // «слов разобрано × слотов пройдено». Жадный проход здесь ошибается:
        // необязательный слот, съевший слово, может лишить его обязательный.
        //
        // В каждой клетке хранится не только сумма совпадений, но и число
        // слов, которые в неё вошли: паразиты пропускаются, и делить сумму
        // на общее количество слов было бы неверно — одно «пожалуйста»
        // роняло бы оценку ниже порога.
        const double Impossible = double.NegativeInfinity;

        var sum = new double[n + 1, m + 1];
        var used = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++)
        {
            for (int j = 0; j <= m; j++) sum[i, j] = Impossible;
        }
        sum[0, 0] = 0;

        void Relax(int i, int j, double value, int count)
        {
            if (value <= sum[i, j]) return;
            sum[i, j] = value;
            used[i, j] = count;
        }

        for (int j = 0; j <= m; j++)
        {
            for (int i = 0; i <= n; i++)
            {
                if (double.IsNegativeInfinity(sum[i, j])) continue;

                // Паразит пропускается на любом месте и ничего не стоит.
                if (i < n && ignorable is not null && ignorable(i))
                    Relax(i + 1, j, sum[i, j], used[i, j]);

                if (j >= m) continue;
                var slot = Slots[j];

                // Слот пропущен — так можно только с необязательным.
                if (slot.Optional) Relax(i, j + 1, sum[i, j], used[i, j]);

                if (i >= n) continue;

                var similarity = slot.Similarity(spokenKeys[i]);
                if (similarity <= (slot.Optional ? SlotFloor : RequiredFloor)) continue;

                Relax(i + 1, j + 1, sum[i, j] + similarity, used[i, j] + 1);
            }
        }

        var total = sum[n, m];
        var count = used[n, m];

        if (double.IsNegativeInfinity(total) || count == 0) return 0;

        matched = count;
        return total / count;
    }

    /// <summary>
    /// То же, но без оглядки на порядок слов.
    ///
    /// Русский порядок свободный, и «настройки мне открой» — такая же просьба,
    /// как «открой настройки». Слоты же идут цепочкой и сами этого не знают.
    ///
    /// Способ дороже и грубее: каждому слоту подбирается лучшее из ещё
    /// не занятых слов. Поэтому он идёт вторым, после обычного разбора,
    /// и с более высоким порогом — при прочих равных выигрывает та команда,
    /// которую человек произнёс в привычном порядке.
    /// </summary>
    public double MatchUnordered(string[][] spokenKeys, Func<int, bool>? ignorable = null)
        => MatchUnordered(spokenKeys, out _, ignorable);

    public double MatchUnordered(string[][] spokenKeys, out int matched, Func<int, bool>? ignorable = null)
    {
        matched = 0;

        var n = spokenKeys.Length;
        if (n == 0) return 0;

        var taken = new bool[n];
        double sum = 0;
        var count = 0;

        // Обязательные слоты первыми: им нужнее, а необязательные обойдутся
        // тем, что останется.
        foreach (var slot in Slots.Where(s => !s.Optional).Concat(Slots.Where(s => s.Optional)))
        {
            var bestIndex = -1;
            double bestScore = slot.Optional ? SlotFloor : RequiredFloor;

            for (int i = 0; i < n; i++)
            {
                if (taken[i]) continue;

                var score = slot.Similarity(spokenKeys[i]);
                if (score > bestScore) { bestScore = score; bestIndex = i; }
            }

            if (bestIndex < 0)
            {
                // Необязательному слоту слова не нашлось — это нормально.
                if (slot.Optional) continue;
                return 0;
            }

            taken[bestIndex] = true;
            sum += bestScore;
            count++;
        }

        // Осталось значащее слово — значит, команда другая.
        for (int i = 0; i < n; i++)
        {
            if (taken[i]) continue;
            if (ignorable is not null && ignorable(i)) continue;
            return 0;
        }

        if (count == 0) return 0;

        matched = count;
        return sum / count;
    }
}
