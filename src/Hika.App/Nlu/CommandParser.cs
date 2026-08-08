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
        "запусти", "запустить", "запускай", "запуск",
        "включи", "включить", "включай",
        "вруби", "врубай", "врубить", "врубани",
        "зайди", "зайти", "заходи",
        "перейди", "перейти",
        "покажи", "показать", "показывай",
        "стартуй", "поставь", "загрузи", "вызови", "дай",
        "open", "launch", "start", "run", "show", "goto", "execute", "bring", "fire",
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
        "пожалуйста", "плиз", "давай", "ну", "там", "короче", "вот", "это",
        "мне", "мой", "моя", "моё", "мою", "ка", "же", "бы", "как", "тип",
        "э", "эм", "ммм", "мм", "аа", "эээ",
        "please", "uh", "um", "like", "just", "the", "a", "my",
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
            "закрой окно", "закрой это", "закрой", "close window", "close it",
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

    /// <summary>Порог для готовых команд. Высокий намеренно: перехват запуска программ хуже, чем пропуск команды.</summary>
    private const double FixedCommandThreshold = 0.80;

    public static Intent Parse(string text)
    {
        var tokens = TextNormalizer.Tokenize(text);
        if (tokens.Length == 0) return Intent.None;

        // Паразиты убираем сразу, но если из фразы ничего не осталось —
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
            if (score > bestScore)
            {
                bestScore = score;
                bestKind = kind;
            }
        }

        if (bestKind == IntentKind.None || bestScore < FixedCommandThreshold) return null;

        Log.Debug($"готовая команда: {bestKind} (оценка {bestScore:F2})", "nlu");
        return new Intent(bestKind, "", bestScore);
    }
}
