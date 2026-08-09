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

    /// <summary>
    /// Начала фраз, после которых человек ждёт поисковую выдачу.
    ///
    /// В отличие от глаголов поиска, здесь фраза уходит в поисковик целиком,
    /// вместе с самим оборотом: запрос «что такое чёрная дыра» ищется лучше,
    /// чем обрубленный «чёрная дыра».
    ///
    /// Список нарочно короткий. Раньше в поиск уходило всё, что не нашлось
    /// в каталоге, и выглядело это так: человек говорит что-то рядом
    /// с компьютером, а браузер открывает его же слова. Поиск должен
    /// случаться, когда о нём попросили, и никогда — «на всякий случай».
    /// </summary>
    private static readonly string[][] SearchOpeners =
    {
        new[] { "что", "такое" },
        new[] { "кто", "такой" },
        new[] { "кто", "такая" },
        new[] { "как" },
        new[] { "how", "to" },
        new[] { "what", "is" },
    };

    /// <summary>
    /// Что может стоять после «как», не будучи поисковым запросом.
    ///
    /// «Как дела» и «как приготовить борщ» начинаются одинаково, а хотят
    /// от программы прямо противоположного.
    /// </summary>
    private static readonly HashSet<string> NotSearchAfterHow = new(StringComparer.Ordinal)
    {
        "дела", "ты", "жизнь", "настроение", "оно", "сам", "сама",
        "думаешь", "считаешь", "поживаешь", "успехи", "здоровье",
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
        // «Пауза» и «продолжи» разведены намеренно. Переключатель хорош
        // на клавиатуре, где человек видит, что играет; сказанное вслух
        // «пауза» означает «пусть замолчит», и включить музыку в ответ —
        // ровно противоположное тому, о чём просили.
        (IntentKind.MediaPause, new[]
        {
            "пауза", "паузу", "поставь на паузу", "стоп", "останови",
            "останови музыку", "выключи музыку", "тихо", "замолчи",
            "хватит", "приостанови", "pause", "stop",
        }),
        (IntentKind.MediaPlay, new[]
        {
            "продолжи", "продолжай", "включи обратно", "верни музыку",
            "давай дальше", "плей", "play", "resume", "continue",
        }),
        (IntentKind.MediaPlayPause, new[]
        {
            "переключи воспроизведение", "play pause", "toggle play",
        }),
        (IntentKind.NowPlaying, new[]
        {
            "что играет", "что сейчас играет", "что за трек", "что за песня",
            "какая песня", "какой трек", "what is playing", "now playing",
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
        (IntentKind.Sleep, new[]
        {
            "спящий режим", "уйди в сон", "усыпи компьютер", "режим сна", "sleep",
        }),
        (IntentKind.Time, new[]
        {
            "который час", "сколько времени", "сколько сейчас времени",
            "текущее время", "what time is it",
        }),
        (IntentKind.NewTab, new[]
        {
            "новая вкладка", "открой вкладку", "новую вкладку", "new tab",
        }),
        (IntentKind.CloseTab, new[]
        {
            "закрой вкладку", "убери вкладку", "close tab",
        }),
        (IntentKind.BrowserBack, new[]
        {
            "вернись назад", "шаг назад", "предыдущая страница", "go back",
        }),
        (IntentKind.BrowserRefresh, new[]
        {
            "обнови страницу", "перезагрузи страницу", "refresh", "reload",
        }),
        (IntentKind.NextDesktop, new[]
        {
            "следующий рабочий стол", "next desktop",
        }),
        (IntentKind.PreviousDesktop, new[]
        {
            "предыдущий рабочий стол", "previous desktop",
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

        // Обороты поиска проверяем до вычистки паразитов — «как» числится
        // среди них, и без этой проверки «как приготовить борщ» потеряло бы
        // ровно то слово, по которому опознаётся.
        if (IsSearchOpener(tokens))
            return new Intent(IntentKind.Search, string.Join(' ', tokens)) { ExplicitVerb = true };

        // Паразиты убираем следом, но если из фразы ничего не осталось —
        // возвращаем исходную: команда могла целиком состоять из таких слов.
        var cleaned = tokens.Where(t => !Fillers.Contains(t)).ToArray();
        if (cleaned.Length == 0) cleaned = tokens;

        // Шаблоны разбираются первыми. Порядок пришлось развернуть: обороты
        // «перейди в …» и «разверни …» задуманы как переключение на окно
        // и перехватывали всё, что с них начинается, — «перейди в начало
        // страницы» становилось попыткой найти окно с таким названием.
        // Точное описание команды должно побеждать общий оборот.
        var pattern = MatchPattern(cleaned);
        if (pattern is not null) return pattern;

        // Команды с числом или названием внутри: «громкость тридцать»,
        // «таймер на пять минут», «переключись на хром».
        var parametrized = MatchParametrized(cleaned);
        if (parametrized is not null) return parametrized;

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

            return new Intent(IntentKind.Search, query.Trim()) { ExplicitVerb = true };
        }

        // Запуск: снимаем глагол, если он есть.
        var start = 0;
        var explicitVerb = LaunchVerbs.Contains(cleaned[0]);
        if (explicitVerb) start = 1;

        var target = cleaned[start..]
            .Where(t => !TargetNoise.Contains(t))
            .ToArray();

        // «Ави, открой» без цели — команды нет.
        if (target.Length == 0)
        {
            return start > 0 ? Intent.None : new Intent(IntentKind.Launch, string.Join(' ', cleaned));
        }

        return new Intent(IntentKind.Launch, string.Join(' ', target)) { ExplicitVerb = explicitVerb };
    }

    /// <summary>
    /// Фраза начинается с оборота, после которого человек ждёт поиск.
    /// </summary>
    private static bool IsSearchOpener(string[] tokens)
    {
        foreach (var opener in SearchOpeners)
        {
            if (tokens.Length <= opener.Length) continue;   // одного оборота мало, нужен сам запрос
            if (!StartsWith(tokens, opener)) continue;

            // «Как дела» — это не запрос в поисковик, чем бы ни кончалась фраза.
            if (opener.Length == 1 && opener[0] == "как" && NotSearchAfterHow.Contains(tokens[1]))
                return false;

            return true;
        }

        return false;
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

    /// <summary>Слова, по которым узнаётся разговор о громкости.</summary>
    private static readonly HashSet<string> VolumeWords = new(StringComparer.Ordinal)
    {
        "громкость", "громкости", "громкостью", "звук", "звука", "volume",
    };

    /// <summary>Слова, по которым узнаётся просьба напомнить через время.</summary>
    private static readonly HashSet<string> TimerWords = new(StringComparer.Ordinal)
    {
        "таймер", "таймере", "напомни", "напоминание", "будильник", "засеки",
        "timer", "remind",
    };

    /// <summary>
    /// Начала фраз, которыми просят набрать текст.
    ///
    /// «Напиши» здесь отсутствует намеренно: этим словом просят сочинить,
    /// а не напечатать, и оно уходит в разговор. Перепутать эти два значения
    /// — значит на просьбу «напиши письмо другу» вбить в активное окно
    /// слова «письмо другу».
    /// </summary>
    private static readonly string[][] TypeOpeners =
    {
        new[] { "напечатай" }, new[] { "печатай" }, new[] { "набери" },
        new[] { "введи" }, new[] { "вбей" }, new[] { "напечатать" },
        new[] { "type" },
    };

    /// <summary>
    /// Слова, которые не могут быть названием окна.
    ///
    /// Обороты переключения — «перейди в», «разверни» — начинают и другие
    /// команды. Окна с названием «начало» или «вкладку» не существует,
    /// а вот «перейди в начало страницы» человек говорит regularly.
    /// </summary>
    private static readonly HashSet<string> NotAWindowName = new(StringComparer.Ordinal)
    {
        "начало", "конец", "верх", "низ", "середину", "самый",
        "окно", "окошко", "вкладку", "вкладка", "страницу", "страница",
        "это", "всё", "все", "назад", "вперёд", "вперед", "влево", "вправо",
        "вверх", "вниз", "полный", "обычный",
    };

    /// <summary>Начала фраз, которыми просят переключиться на уже открытое окно.</summary>
    private static readonly string[][] FocusOpeners =
    {
        new[] { "переключись", "на" },
        new[] { "переключи", "на" },
        new[] { "перейди", "в" },
        new[] { "перейди", "к" },
        new[] { "разверни" },
        new[] { "покажи", "окно" },
        new[] { "вернись", "в" },
        new[] { "switch", "to" },
        new[] { "focus" },
    };

    /// <summary>
    /// Команды, у которых есть содержимое: число или название.
    ///
    /// Готовые команды сравниваются целиком, и такие фразы им не поддаются:
    /// «сделай громкость тридцать» отличается от «сделай тише» не оборотом,
    /// а числом внутри.
    /// </summary>
    private static Intent? MatchParametrized(string[] tokens)
    {
        if (tokens.Length == 0) return null;

        // Громкость: нужны и слово про громкость, и число.
        if (tokens.Any(VolumeWords.Contains))
        {
            var value = Numbers.First(tokens);
            if (value is >= 0 and <= 100)
            {
                Log.Debug($"громкость на {value}", "nlu");
                return new Intent(IntentKind.VolumeSet, value.Value.ToString());
            }
        }

        // Таймер: нужны и слово про напоминание, и промежуток.
        if (tokens.Any(TimerWords.Contains))
        {
            var duration = Numbers.Duration(tokens);
            if (duration is { TotalSeconds: >= 5 and <= 24 * 3600 })
            {
                Log.Debug($"таймер на {duration}", "nlu");
                return new Intent(IntentKind.Timer, ((int)duration.Value.TotalSeconds).ToString());
            }
        }

        // Набор текста: всё после оборота печатается как есть.
        //
        // «Напиши» сюда не входит: этим словом просят сочинить, а не набрать,
        // и уходит оно в разговор. Разница заметна сразу, стоит перепутать.
        foreach (var opener in TypeOpeners)
        {
            if (tokens.Length <= opener.Length) continue;
            if (!StartsWith(tokens, opener)) continue;

            var text = string.Join(' ', tokens[opener.Length..]).Trim();
            if (text.Length == 0) continue;

            return new Intent(IntentKind.TypeText, text) { ExplicitVerb = true };
        }

        // Переключение на окно: оборот плюс то, на что переключаться.
        foreach (var opener in FocusOpeners)
        {
            if (tokens.Length <= opener.Length) continue;
            if (!StartsWith(tokens, opener)) continue;

            var rest = tokens[opener.Length..];

            // «Перейди в начало страницы» начинается точно так же, как
            // «перейди в телеграм», но окна с названием «начало» не бывает.
            // Служебное слово сразу после оборота означает другую команду.
            if (NotAWindowName.Contains(rest[0])) continue;

            var target = string.Join(' ', rest.Where(t => !TargetNoise.Contains(t)));
            if (target.Length == 0) continue;

            return new Intent(IntentKind.FocusWindow, target) { ExplicitVerb = true };
        }

        return null;
    }

    /// <summary>
    /// Союзы, по которым фраза может распадаться на несколько команд.
    ///
    /// «Ави, открой стим и сделай потише» — две просьбы в одном предложении,
    /// и это самая обычная человеческая речь. Резать по ним вслепую нельзя:
    /// «Гарри Поттер и узник Азкабана» — одно название, а не две команды.
    /// Поэтому здесь только предлагаются места разреза, а решение принимается
    /// выше — там, где известно, находится ли каждая часть в каталоге.
    /// </summary>
    private static readonly string[] Conjunctions =
    {
        "и", "а", "потом", "затем", "плюс", "также", "ещё", "еще",
        "and", "then", "also",
    };

    /// <summary>
    /// Делит фразу на возможные команды. Одна часть — значит, делить нечего.
    /// </summary>
    public static IReadOnlyList<string> Segments(string text)
    {
        var tokens = TextNormalizer.Tokenize(text);
        if (tokens.Length < 4) return new[] { text };

        var parts = new List<string>();
        var current = new List<string>();

        foreach (var token in tokens)
        {
            if (Conjunctions.Contains(token, StringComparer.Ordinal))
            {
                // Союз в самом начале — это затравка («а открой стим»),
                // а не разделитель. И слишком короткий кусок командой не бывает.
                if (current.Count >= 2)
                {
                    parts.Add(string.Join(' ', current));
                    current.Clear();
                    continue;
                }

                if (current.Count == 0) continue;
            }

            current.Add(token);
        }

        if (current.Count > 0) parts.Add(string.Join(' ', current));

        // Хвост из одного слова — почти наверняка часть предыдущего названия,
        // а не отдельная команда.
        if (parts.Count > 1 && parts[^1].Split(' ').Length == 1 && parts[^1].Length < 4)
            return new[] { text };

        return parts.Count > 1 ? parts : new[] { text };
    }

    /// <summary>Ищет самый подходящий шаблон.</summary>
    private static Intent? MatchPattern(string[] tokens)
    {
        var spoken = tokens.Select(Translit.Keys).ToArray();

        IntentKind bestKind = IntentKind.None;
        double bestScore = 0;

        foreach (var pattern in CommandCatalog.All)
        {
            var score = pattern.Match(spoken);
            if (score < pattern.Threshold) continue;

            if (score > bestScore)
            {
                bestScore = score;
                bestKind = pattern.Kind;
            }
        }

        if (bestKind == IntentKind.None) return null;

        Log.Debug($"шаблон: {bestKind} (оценка {bestScore:F2})", "nlu");
        return new Intent(bestKind, "", bestScore);
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

            // И наоборот: одно слово не может заменить двухсловную команду.
            // Без этого правила «открой» совпадало с «открой вкладку»
            // и превращало оборванную фразу в новую вкладку браузера.
            // Короткие формы у всех команд перечислены отдельно, так что
            // ничего нужного правило не отсекает.
            if (tokens.Length == 1 && words > 1) continue;

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
