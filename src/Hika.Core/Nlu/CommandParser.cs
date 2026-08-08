using Hika.Diagnostics;

namespace Hika.Nlu;

/// <summary>
/// Превращает сказанное после имени в намерение.
///
/// Разбор нарочно устроен просто и без модели: команды вида «открой ютуб»
/// не требуют понимания языка, а требуют мгновенности. Языковая модель добавила
/// бы к каждой команде секунду ожидания и зависимость от интернета — ради задачи,
/// которую решают несколько сотен строк со списком глаголов.
/// </summary>
public static class CommandParser
{
    /// <summary>Глаголы запуска. Снимаются перед тем, как искать цель в каталоге.</summary>
    private static readonly HashSet<string> LaunchVerbs = new(StringComparer.Ordinal)
    {
        "открой", "открыть", "открывай", "откр", "отрой",
        "откроешь", "откроете", "открыл", "открыла", "откройте",
        "запусти", "запустить", "запускай", "запуск", "запустишь", "запустите",
        "включи", "включить", "включай", "включишь", "включите",
        "вруби", "врубай", "врубить", "врубани", "врубишь",
        "зайди", "зайти", "заходи", "зайдём", "зайдем",
        "перейди", "перейти", "переключись",
        "покажи", "показать", "показывай", "покажешь", "покажите",
        "стартуй", "поставь", "поставить", "загрузи", "загрузить",
        "вызови", "вызвать", "дай", "найдись", "открывается",
        "open", "launch", "start", "run", "show", "goto", "execute", "bring", "fire", "pull",
    };

    /// <summary>
    /// Вежливые обороты и модальные обёртки, которыми люди на самом деле говорят.
    ///
    /// «Ави, можешь открыть-ка мне стим» — так звучит живая просьба, и делать
    /// вид, что человек будет диктовать «открой стим» ровным голосом робота,
    /// значит проиграть заранее. Снимаются с начала фразы, по одному обороту
    /// за проход, пока снимается.
    /// </summary>
    private static readonly string[][] PolitePrefixes =
    {
        new[] { "не", "мог", "бы", "ты" },
        new[] { "не", "могла", "бы", "ты" },
        new[] { "не", "могли", "бы", "вы" },
        new[] { "не", "мог", "бы" },
        new[] { "не", "могла", "бы" },
        new[] { "не", "могли", "бы" },
        new[] { "будь", "добр" },
        new[] { "будь", "добра" },
        new[] { "будьте", "добры" },
        new[] { "будь", "другом" },
        new[] { "сделай", "одолжение" },
        new[] { "мне", "нужно" },
        new[] { "мне", "надо" },
        new[] { "я", "хочу" },
        new[] { "хочу", "чтобы", "ты" },
        new[] { "можешь", "ли", "ты" },
        new[] { "можешь", "ли" },
        new[] { "можно", "ли" },
        new[] { "можешь" }, new[] { "можете" },
        new[] { "сможешь" }, new[] { "сможете" },
        new[] { "можно" }, new[] { "надо" }, new[] { "нужно" }, new[] { "хочу" },
        new[] { "ну-ка" }, new[] { "давай-ка" },
        new[] { "ты" }, new[] { "а" }, new[] { "и" },
        new[] { "can", "you" }, new[] { "could", "you" }, new[] { "would", "you" },
        new[] { "i", "want", "to" }, new[] { "i", "need", "to" },
        new[] { "let's" }, new[] { "lets" },
    };

    /// <summary>Глаголы поиска. Всё после них уходит в поисковик как есть.</summary>
    private static readonly HashSet<string> SearchVerbs = new(StringComparer.Ordinal)
    {
        "найди", "найти", "поищи", "поискать", "ищи", "поиск",
        "загугли", "гугли", "погугли", "гугл",
        "search", "google", "find", "lookup",
    };

    /// <summary>Слова-паразиты и вежливость: на смысл не влияют, сопоставлению мешают.</summary>
    private static readonly HashSet<string> Fillers = new(StringComparer.Ordinal)
    {
        "пожалуйста", "плиз", "давай", "давайте", "ну", "там", "короче", "вот", "это",
        "мне", "нам", "мой", "моя", "моё", "мою", "мои", "свой", "свою",
        "ка", "же", "бы", "как", "тип", "типа", "блин", "слушай", "смотри",
        "быстро", "быстренько", "срочно", "сейчас", "щас", "пока",
        "э", "эм", "ммм", "мм", "аа", "эээ", "ой",
        "please", "uh", "um", "like", "just", "the", "a", "my", "our", "quickly",
    };

    /// <summary>Уточнения, которые человек добавляет к цели, но каталогу они мешают.</summary>
    private static readonly HashSet<string> TargetNoise = new(StringComparer.Ordinal)
    {
        "сайт", "сайтик", "страницу", "страница", "приложение", "программу",
        "программа", "прогу", "site", "website", "page", "app", "application",
    };

    /// <summary>
    /// Готовые команды. Сравниваются целиком и с высоким порогом: они должны
    /// срабатывать только на явное совпадение, а не перехватывать запуск программ.
    /// </summary>
    private static readonly (IntentKind Kind, string[] Phrases)[] FixedCommands =
    {
        (IntentKind.VolumeUp, new[]
        {
            "громче", "сделай громче", "прибавь звук", "прибавь громкость",
            "увеличь громкость", "погромче", "volume up", "louder", "turn it up",
        }),
        (IntentKind.VolumeDown, new[]
        {
            "тише", "сделай тише", "убавь звук", "убавь громкость",
            "уменьши громкость", "потише", "volume down", "quieter", "turn it down",
        }),
        (IntentKind.VolumeMute, new[]
        {
            "выключи звук", "отключи звук", "заглуши", "без звука", "мьют",
            "приглуши", "mute", "silence",
        }),
        (IntentKind.MediaPlayPause, new[]
        {
            "пауза", "поставь на паузу", "останови", "продолжи", "плей",
            "pause", "play", "resume",
        }),
        (IntentKind.MediaNext, new[]
        {
            "следующий трек", "следующая песня", "переключи трек", "дальше",
            "next track", "next song", "skip",
        }),
        (IntentKind.MediaPrevious, new[]
        {
            "предыдущий трек", "предыдущая песня", "верни трек", "назад трек",
            "previous track", "previous song",
        }),
        (IntentKind.LockWorkstation, new[]
        {
            "заблокируй", "заблокируй компьютер", "заблокируй экран", "блокировка",
            "lock", "lock pc", "lock screen",
        }),
        (IntentKind.ShowDesktop, new[]
        {
            "сверни все", "сверни всё", "покажи рабочий стол", "рабочий стол",
            "show desktop", "minimize all",
        }),
        (IntentKind.MinimizeWindow, new[]
        {
            "сверни окно", "сверни", "спрячь окно", "minimize", "minimize window",
        }),
        (IntentKind.CloseWindow, new[]
        {
            // Голого «закрой» здесь намеренно нет: от «открой» оно отличается
            // одной буквой, и односложная команда перехватывала бы каждый
            // запуск программы.
            "закрой окно", "закрой это", "close window", "close it",
        }),
        (IntentKind.Screenshot, new[]
        {
            "сделай скриншот", "скриншот", "снимок экрана", "сфоткай экран",
            "screenshot", "take a screenshot", "capture screen",
        }),
    };

    /// <summary>Заранее разобранные написания готовых команд.</summary>
    private static readonly (IntentKind Kind, string[][] Keys, int Words)[] FixedCommandKeys =
        FixedCommands
            .SelectMany(c => c.Phrases.Select(p =>
            {
                var tokens = TextNormalizer.Tokenize(p);
                return (c.Kind, Keys: tokens.Select(Translit.Keys).ToArray(), Words: tokens.Length);
            }))
            .ToArray();

    /// <summary>
    /// Порог для готовых команд. Высокий намеренно: перехватить запуск программы
    /// хуже, чем не расслышать команду — во втором случае человек просто повторит.
    /// </summary>
    private const double FixedCommandThreshold = 0.80;

    /// <summary>
    /// Для команд из одного слова порог выше. Короткие слова слишком легко
    /// спутать: «громче» и «горче» — одна буква, и такой запас нужен,
    /// чтобы односложные команды не срабатывали на всё подряд.
    /// </summary>
    private const double SingleWordThreshold = 0.90;

    public static Intent Parse(string text)
    {
        var tokens = TextNormalizer.Tokenize(text);
        if (tokens.Length == 0) return Intent.None;

        // Вежливые обороты снимаем до паразитов: они многословные, и по одному
        // слову их не опознать — «не мог бы ты» состоит сплошь из безобидных слов.
        tokens = StripPolitePrefixes(tokens);
        if (tokens.Length == 0) return Intent.None;

        // Паразиты убираем следом, но если из фразы ничего не осталось —
        // возвращаем исходную: команда могла целиком состоять из таких слов.
        var cleaned = tokens.Where(t => !Fillers.Contains(t)).ToArray();
        if (cleaned.Length == 0) cleaned = tokens;

        var fixedMatch = MatchFixedCommand(cleaned);
        if (fixedMatch is not null) return fixedMatch;

        // Поиск в интернете
        if (SearchVerbs.Contains(cleaned[0]))
        {
            var query = string.Join(' ', cleaned[1..]).Trim();

            // «гугл» без продолжения — это просьба открыть Google, а не искать пустоту.
            if (query.Length == 0) return new Intent(IntentKind.Launch, cleaned[0]);

            // «найди в гугле котиков» — предлог с названием поисковика отбрасываем.
            var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 2 && words[0] is "в" or "на" or "in" or "on")
                query = string.Join(' ', words[2..]);

            return new Intent(IntentKind.Search, query.Trim());
        }

        // Запуск: снимаем глагол, если он есть.
        var start = 0;
        if (LaunchVerbs.Contains(cleaned[0])) start = 1;

        var target = cleaned[start..]
            .Where(t => !TargetNoise.Contains(t))
            .ToArray();

        // «Ави, открой» без цели — команды нет.
        if (target.Length == 0)
        {
            return start > 0 ? Intent.None : new Intent(IntentKind.Launch, string.Join(' ', cleaned));
        }

        return new Intent(IntentKind.Launch, string.Join(' ', target));
    }

    /// <summary>
    /// Снимает вежливые обороты с начала фразы. По одному за проход,
    /// не больше трёх: «слушай, не мог бы ты открыть мне стим» — предел
    /// разумного, дальше начинается уже не команда.
    /// </summary>
    private static string[] StripPolitePrefixes(string[] tokens)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            var stripped = false;

            foreach (var prefix in PolitePrefixes)
            {
                if (tokens.Length <= prefix.Length) continue;   // фраза не может состоять из одной вежливости
                if (!StartsWith(tokens, prefix)) continue;

                tokens = tokens[prefix.Length..];
                stripped = true;
                break;
            }

            if (!stripped) break;
        }

        return tokens;
    }

    private static bool StartsWith(string[] tokens, string[] prefix)
    {
        for (int i = 0; i < prefix.Length; i++)
        {
            if (tokens[i] != prefix[i]) return false;
        }
        return true;
    }

    private static Intent? MatchFixedCommand(string[] tokens)
    {
        var spokenKeys = tokens.Select(Translit.Keys).ToArray();

        IntentKind bestKind = IntentKind.None;
        double bestScore = 0;

        foreach (var (kind, keys, words) in FixedCommandKeys)
        {
            // Готовая команда описывает фразу целиком. Если сказано заметно
            // больше слов, это уже что-то другое — скорее всего запуск программы.
            if (tokens.Length > words + 1) continue;

            var score = FuzzyMatch.PhraseSimilarity(spokenKeys, keys);
            var threshold = words == 1 ? SingleWordThreshold : FixedCommandThreshold;
            if (score < threshold) continue;

            if (score > bestScore)
            {
                bestScore = score;
                bestKind = kind;
            }
        }

        if (bestKind == IntentKind.None) return null;

        Log.Debug($"готовая команда: {bestKind} (оценка {bestScore:F2})", "nlu");
        return new Intent(bestKind, "", bestScore);
    }
}
