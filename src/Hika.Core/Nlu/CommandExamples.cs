namespace Hika.Nlu;

/// <summary>Одна фраза, которую можно сказать, и что от неё будет.</summary>
public sealed record CommandExample(string Say, IntentKind Kind, string Does);

/// <summary>Раздел списка команд.</summary>
public sealed record CommandGroup(string Title, string Hint, IReadOnlyList<CommandExample> Examples);

/// <summary>
/// Что ей можно сказать — список, который человек видит глазами.
///
/// Нужен по простой причине: полтораста команд невозможно помнить, а спросить
/// не у кого. Читать документацию в репозитории человек не будет, и правильно
/// сделает — он ставил программу, а не подписывался на чтение.
///
/// Список написан руками, а не собран из шаблонов: сгенерированные примеры
/// получаются вида «сверни всё окна» и читаются как ошибка, а не как
/// подсказка. Но каждый пример здесь проверяется тестом на то, что он
/// действительно разбирается в обещанную команду. Подсказка, которая врёт, —
/// хуже отсутствующей: человек говорит написанное, ничего не происходит,
/// и он делает вывод про всю программу целиком.
/// </summary>
public static class CommandExamples
{
    public static readonly CommandGroup[] All =
    {
        new("Программы и сайты",
            "Работает всё, что установлено: меню «Пуск» и приложения Windows она обходит сама.",
            new CommandExample[]
            {
                new("открой стим", IntentKind.Launch, "запустит программу"),
                new("запусти фотошоп", IntentKind.Launch, "по названию, даже неточному"),
                new("открой ютуб", IntentKind.Launch, "откроет сайт"),
                new("переключись на хром", IntentKind.FocusWindow, "вернёт уже открытое окно"),
            }),

        new("Диктовка",
            "Всё сказанное набирается в активное окно. Знаки препинания можно называть вслух.",
            new CommandExample[]
            {
                new("диктую", IntentKind.DictationStart, "дальше имя не нужно"),
                new("записывай за мной", IntentKind.DictationStart, "то же самое"),
                new("хватит печатать", IntentKind.DictationStop, "закончить"),
                new("напечатай привет", IntentKind.TypeText, "набрать одну фразу"),
            }),

        new("Музыка и видео",
            "Пауза достаётся тому, кто действительно звучит, а не уходит наугад мультимедийной клавишей.",
            new CommandExample[]
            {
                new("пауза", IntentKind.MediaPause, "остановить"),
                new("продолжи", IntentKind.MediaPlay, "дальше с того же места"),
                new("следующий трек", IntentKind.MediaNext, "переключить"),
                new("включи музыку", IntentKind.PlayMusic, "поднять плеер и играть"),
                new("что играет", IntentKind.NowPlaying, "скажет вслух"),
            }),

        new("Видео: ютуб и любой плеер",
            "Клавиши уходят тому окну, которое показывает видео. Если оно свёрнуто — она его поднимет.",
            new CommandExample[]
            {
                new("перемотай назад", IntentKind.MediaSeekBackward, "на несколько секунд"),
                new("перемотай на минуту", IntentKind.MediaSeekForwardFar, "подальше"),
                new("включи субтитры", IntentKind.MediaCaptions, "и выключит тоже"),
                new("разверни видео", IntentKind.MediaFullScreen, "во весь экран"),
                new("следующее видео", IntentKind.NextVideo, "в ютубе"),
                new("сделай быстрее", IntentKind.MediaSpeedUp, "скорость воспроизведения"),
            }),

        new("Звук и яркость",
            "Громкость меняется напрямую, а не нажатием клавиш: шаг задаём мы, а не производитель клавиатуры.",
            new CommandExample[]
            {
                new("громче", IntentKind.VolumeUp, "на восемь процентов"),
                new("тише", IntentKind.VolumeDown, ""),
                new("выключи звук", IntentKind.VolumeMute, "и вернёт обратно"),
                new("сделай громкость тридцать", IntentKind.VolumeSet, "числом"),
                new("сделай ярче", IntentKind.BrightnessUp, "экран"),
            }),

        new("Окна",
            "",
            new CommandExample[]
            {
                new("закрой окно", IntentKind.CloseWindow, "аккуратно, с вопросом о несохранённом"),
                new("сверни окно", IntentKind.MinimizeWindow, ""),
                new("сверни всё", IntentKind.ShowDesktop, "показать рабочий стол"),
                new("покажи все окна", IntentKind.TaskView, "обзор задач"),
                new("прижми окно влево", IntentKind.SnapLeft, "на половину экрана"),
                new("следующий рабочий стол", IntentKind.NextDesktop, ""),
            }),

        new("Браузер",
            "",
            new CommandExample[]
            {
                new("новая вкладка", IntentKind.NewTab, ""),
                new("закрой вкладку", IntentKind.CloseTab, ""),
                new("верни закрытую вкладку", IntentKind.ReopenTab, ""),
                new("следующая вкладка", IntentKind.NextTab, ""),
                new("назад", IntentKind.BrowserBack, "шаг назад по страницам"),
                new("обнови страницу", IntentKind.BrowserRefresh, ""),
                new("добавь в закладки", IntentKind.Bookmark, ""),
            }),

        new("Текст и прокрутка",
            "",
            new CommandExample[]
            {
                new("скопируй", IntentKind.Copy, ""),
                new("вставь", IntentKind.Paste, ""),
                new("отмени действие", IntentKind.Undo, ""),
                new("выдели всё", IntentKind.SelectAll, ""),
                new("сохрани файл", IntentKind.Save, ""),
                new("прокрути вниз", IntentKind.ScrollDown, ""),
                new("найди на странице", IntentKind.FindOnPage, ""),
            }),

        new("Система",
            "Выключения и перезагрузки здесь нет намеренно: цена одной ошибки — несохранённая работа за день.",
            new CommandExample[]
            {
                new("который час", IntentKind.Time, "скажет вслух"),
                new("сколько заряда осталось", IntentKind.Battery, ""),
                new("сделай скриншот", IntentKind.Screenshot, "вызовет «Ножницы»"),
                new("заблокируй компьютер", IntentKind.LockWorkstation, ""),
                new("спящий режим", IntentKind.Sleep, "обратим движением мыши"),
                new("поставь таймер на пять минут", IntentKind.Timer, "напомнит голосом"),
                new("открой проводник", IntentKind.OpenExplorer, ""),
            }),

        new("Поиск и разговор",
            "В поиск уходит только то, о чём попросили этими словами. Всё остальное — нет.",
            new CommandExample[]
            {
                new("загугли погоду в москве", IntentKind.Search, "откроет выдачу"),
                new("что такое чёрная дыра", IntentKind.Search, "оборот остаётся в запросе"),
            }),
    };

    /// <summary>Все примеры одним списком — для проверок.</summary>
    public static IEnumerable<CommandExample> Flat() => All.SelectMany(g => g.Examples);
}
