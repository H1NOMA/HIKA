using Hika.Config;
using Hika.Diagnostics;
using Hika.Nlu;

namespace Hika.Wake;

public sealed record WakeMatch(bool Matched, string Word, string Rest, double Score)
{
    public static readonly WakeMatch None = new(false, "", "", 0);

    /// <summary>Позвали по имени, но команды не сказали — значит, ждём продолжения.</summary>
    public bool IsBareCall => Matched && string.IsNullOrWhiteSpace(Rest);
}

/// <summary>
/// Ищет слово пробуждения в начале фразы.
///
/// Задача сложнее, чем кажется, потому что «Ави» и «Хика» — не слова русского
/// языка, и Whisper коверкает их постоянно: «Авви», «Авиа», «Обои», «Хико»,
/// «Кика», «Chica». Поэтому сравнение нечёткое и по звучанию, а не по буквам,
/// и допустимое расхождение зависит от длины слова — трёхбуквенному «Ави»
/// прощается ровно одна ошибка, иначе оно начнёт совпадать с каждым вторым словом.
/// </summary>
public sealed class WakeWordMatcher
{
    /// <summary>Обращения, которые люди ставят перед именем. Снимаются перед проверкой.</summary>
    private static readonly HashSet<string> Greetings = new(StringComparer.Ordinal)
    {
        "привет", "приветик", "здравствуй", "здравствуйте", "хай",
        "эй", "хэй", "хей", "слушай", "послушай", "слышь",
        "окей", "ок", "окэй", "хорошо",
        "hey", "hi", "hello", "ok", "okay", "yo", "listen",
    };

    /// <summary>
    /// Частые короткие слова, похожие на «Ави» по звучанию. Для них требуем
    /// точное совпадение — иначе ассистент будет просыпаться от обычной речи.
    /// </summary>
    private static readonly HashSet<string> RiskyLookalikes = new(StringComparer.Ordinal)
    {
        // Похожие на «Ави»
        "они", "оны", "иди", "или", "обе", "эти", "эта", "это", "как", "так",
        "там", "вот", "две", "три", "аби", "обои", "увы", "уви", "иви", "ави́",
        "все", "уже", "еще", "over", "avia", "audio",

        // Похожие на «Хика» и «Хико». Порог намеренно щедрый, и слова,
        // начинающиеся на «хи», от него страдают первыми.
        "хиты", "хитро", "хитрый", "хилый", "химия", "хижина", "хинди",
        "тихо", "лихо", "пока", "щека", "щеки",
    };

    /// <summary>
    /// Написания, которые встречаются в выдаче распознавателя достаточно часто,
    /// чтобы прописать их руками, а не полагаться на нечёткое сравнение.
    /// </summary>
    private static readonly Dictionary<string, string[]> HandTunedVariants = new(StringComparer.Ordinal)
    {
        ["ави"] = new[]
        {
            "avi", "ave", "avee", "avy", "avie", "ivy",
            "авви", "авия", "авиа", "эви", "ави", "авй", "аби", "авик",
        },
        ["хика"] = new[]
        {
            "hika", "heeka", "hicka", "chica", "kika", "hikka",
            "хикка", "хиика", "гика", "шика", "чика", "хиха", "кика", "хика",
        },
        ["хико"] = new[]
        {
            "hiko", "heeko", "hicko", "chico", "kiko", "hikko",
            "хикко", "хиико", "гико", "шико", "чико", "хихо", "кико",
            "хиког", "хикою", "хикой", "хико",
        },
    };

    private readonly List<WakeEntry> _entries = new();
    private readonly double _tolerance;
    private readonly bool _allowAnywhere;

    private sealed record WakeEntry(string Canonical, string Form, string[] Keys, int AllowedDistance);

    public IReadOnlyList<string> Words => _entries.Select(e => e.Canonical).Distinct().ToList();

    public WakeWordMatcher(WakeConfig config)
    {
        _tolerance = Math.Clamp(config.Tolerance, 0.0, 0.75);
        _allowAnywhere = config.AllowAnywhere;

        foreach (var word in config.Words ?? new List<string>())
        {
            var canonical = TextNormalizer.Normalize(word);
            if (canonical.Length < 2) continue;

            AddForm(canonical, canonical);

            if (HandTunedVariants.TryGetValue(canonical, out var tuned))
            {
                foreach (var variant in tuned) AddForm(canonical, TextNormalizer.Normalize(variant));
            }
        }

        // Дополнения из настроек привязываем к первому слову — они нужны для того,
        // чтобы дописать реально услышанное, а не для отдельной сущности.
        var primary = _entries.FirstOrDefault()?.Canonical ?? "ави";
        foreach (var extra in config.ExtraVariants ?? new List<string>())
        {
            var normalized = TextNormalizer.Normalize(extra);
            if (normalized.Length >= 2) AddForm(primary, normalized);
        }

        Log.Info($"слова пробуждения: {string.Join(", ", Words)} ({_entries.Count} написаний)", "wake");
    }

    private void AddForm(string canonical, string form)
    {
        if (form.Length < 2) return;
        if (_entries.Any(e => e.Form == form)) return;

        // Чем короче слово, тем меньше ошибок ему прощается.
        //
        // Округление, а не отбрасывание дробной части: при отбрасывании
        // четырёхбуквенному «Хико» доставалась бы ровно одна ошибка при любом
        // разумном пороге, и настройка tolerance на нём просто не работала бы.
        var allowed = Math.Max(1, (int)Math.Round(form.Length * _tolerance, MidpointRounding.AwayFromZero));

        // Трём буквам больше одной ошибки не прощаем никогда: «ави» и так
        // совпадает с половиной коротких слов языка.
        if (form.Length <= 3) allowed = 1;

        _entries.Add(new WakeEntry(canonical, form, Translit.Keys(form), allowed));
    }

    public WakeMatch Match(string text)
    {
        var tokens = TextNormalizer.Tokenize(text);
        if (tokens.Length == 0) return WakeMatch.None;

        // Снимаем обращение: «Привет, Ави, открой ютуб» -> «Ави, открой ютуб».
        var start = 0;
        while (start < tokens.Length && start < 2 && Greetings.Contains(tokens[start])) start++;
        if (start >= tokens.Length) return WakeMatch.None;

        var limit = _allowAnywhere ? tokens.Length : start + 1;

        for (int i = start; i < limit && i < tokens.Length; i++)
        {
            // Обычный случай: одно слово.
            var direct = TryToken(tokens[i]);
            if (direct is not null)
                return new WakeMatch(true, direct.Value.Word, Join(tokens, i + 1), direct.Value.Score);

            // Распознаватель мог разорвать имя: «а ви».
            if (i + 1 < tokens.Length)
            {
                var glued = TryToken(tokens[i] + tokens[i + 1]);
                if (glued is not null)
                    return new WakeMatch(true, glued.Value.Word, Join(tokens, i + 2), glued.Value.Score);
            }

            // Или наоборот слепить имя с командой: «авиоткрой».
            var split = TrySplit(tokens[i]);
            if (split is not null)
            {
                var rest = split.Value.Tail;
                var tail = Join(tokens, i + 1);
                if (tail.Length > 0) rest = rest + " " + tail;
                return new WakeMatch(true, split.Value.Word, rest.Trim(), split.Value.Score);
            }
        }

        return WakeMatch.None;
    }

    private (string Word, double Score)? TryToken(string token)
    {
        if (token.Length < 2) return null;

        var risky = RiskyLookalikes.Contains(token);
        var keys = Translit.Keys(token);

        (string Word, double Score)? best = null;

        foreach (var entry in _entries)
        {
            // Слово из «опасного» списка принимаем только при точном совпадении.
            if (risky)
            {
                if (token == entry.Form) return (entry.Canonical, 1.0);
                continue;
            }

            var distance = MinDistance(keys, entry.Keys);
            if (distance > entry.AllowedDistance) continue;

            // Первая буква почти никогда не теряется — если она разная,
            // это скорее всего другое слово.
            if (entry.Form.Length <= 4 && token[0] != entry.Form[0] && distance > 0)
            {
                var latinToken = Translit.ToLatin(token);
                var latinForm = Translit.ToLatin(entry.Form);
                if (latinToken.Length == 0 || latinForm.Length == 0) continue;
                if (latinToken[0] != latinForm[0]) continue;
            }

            var score = 1.0 - (double)distance / Math.Max(1, entry.Form.Length);
            if (best is null || score > best.Value.Score) best = (entry.Canonical, score);
        }

        return best;
    }

    /// <summary>Отделяет имя, слипшееся с началом команды.</summary>
    private (string Word, string Tail, double Score)? TrySplit(string token)
    {
        if (token.Length < 6) return null;

        foreach (var entry in _entries)
        {
            var length = entry.Form.Length;
            if (token.Length < length + 3) continue;

            // Берём столько же букв, сколько в имени, плюс-минус одну.
            // Порядок важен: сначала точная длина, иначе «авиоткрой» разрежется
            // как «ав» + «иоткрой» вместо «ави» + «открой» — распознается всё
            // равно, но команда достанется разбору в исковерканном виде.
            foreach (var take in new[] { length, length + 1, length - 1 })
            {
                if (take < 2 || take > token.Length - 3) continue;

                var head = token[..take];
                var distance = MinDistance(Translit.Keys(head), entry.Keys);
                if (distance > Math.Min(1, entry.AllowedDistance)) continue;

                var score = 1.0 - (double)distance / Math.Max(1, length);
                return (entry.Canonical, token[take..], score);
            }
        }

        return null;
    }

    private static int MinDistance(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var best = int.MaxValue;
        foreach (var x in a)
        {
            foreach (var y in b)
            {
                var d = FuzzyMatch.Distance(x, y, best);
                if (d < best) best = d;
                if (best == 0) return 0;
            }
        }
        return best;
    }

    private static string Join(string[] tokens, int from)
        => from >= tokens.Length ? "" : string.Join(' ', tokens[from..]);
}
