namespace Hika.Nlu;

/// <summary>
/// Все команды, описанные устройством фразы.
///
/// Разделы взяты не с потолка. Голосовое управление Windows, распознавание
/// речи Windows и известные ассистенты сходятся на одном и том же наборе:
/// приложения и окна, взаимодействие с экраном, мышь и клавиши, работа
/// с текстом, система, браузер, воспроизведение. Сходятся потому, что набор
/// отражает не устройство программы, а то, из чего состоит работа
/// за компьютером.
///
/// Каждая команда — не список фраз, а слоты: место для глагола, место для
/// уточнения, место для предмета. Перечислять формулировки бессмысленно:
/// сколько ни выпиши, следующую человек скажет мимо списка. Слоты покрывают
/// и то, чего никто не писал.
///
/// ПОРЯДОК ВАЖЕН. При равной оценке побеждает тот, кто стоит выше, поэтому
/// определённое идёт раньше общего: «следующая вкладка» должна разбираться
/// как вкладка, а не как следующий трек.
///
/// ПОРОГИ РАЗНЫЕ, и это не небрежность. Ошибка на «следующей песне» стоит
/// одного лишнего нажатия. Ошибка на «закрой окно» — потерянной работы.
/// Ошибка, перехватившая запуск программы, раздражает чаще всех прочих.
/// </summary>
public static class CommandCatalog
{
    /// <summary>Обычная строгость: команда узнаётся уверенно, но с запасом на оговорку.</summary>
    private const double Normal = 0.82;

    /// <summary>Строже обычного — там, где ошибка что-то ломает или закрывает.</summary>
    private const double Careful = 0.88;

    /// <summary>Мягче обычного — там, где ошибка стоит одного лишнего нажатия.</summary>
    private const double Loose = 0.78;

    public static readonly PhrasePattern[] All =
    {
        // ---- Вкладки браузера ---------------------------------------------
        //
        // Стоят первыми: «следующая вкладка» и «следующая песня» отличаются
        // только последним словом, и вкладка должна выигрывать за счёт того,
        // что назвала предмет явно.

        PhrasePattern.Of(IntentKind.NewTab, Normal,
            "?открой|создай|давай|сделай|new",
            "новая|новую|создай|new",
            "вкладка|вкладку|tab"),

        PhrasePattern.Of(IntentKind.CloseTab, Careful,
            "закрой|убери|закрыть|close",
            "вкладку|вкладка|эту",
            "?вкладку"),

        PhrasePattern.Of(IntentKind.ReopenTab, Normal,
            "верни|восстанови|открой|reopen",
            "?закрытую|последнюю|обратно",
            "вкладку|вкладка|tab"),

        PhrasePattern.Of(IntentKind.NextTab, Normal,
            "?переключи|давай|перейди",
            "следующая|следующую|следующий|next",
            "вкладка|вкладку|tab"),

        PhrasePattern.Of(IntentKind.PreviousTab, Normal,
            "?переключи|давай|перейди",
            "предыдущая|предыдущую|прошлую|previous",
            "вкладка|вкладку|tab"),

        // ---- Воспроизведение -----------------------------------------------

        PhrasePattern.Of(IntentKind.MediaNext, Loose,
            "?давай|включи|поставь|ставь|next",
            "следующий|следующая|следующее|следующую|дальше|переключи|перемотай|next|skip",
            "?трек|треки|песню|песня|песни|композицию|track|song"),

        PhrasePattern.Of(IntentKind.MediaPrevious, Loose,
            "?давай|включи|поставь|верни",
            "предыдущий|предыдущая|предыдущую|прошлый|прошлую|previous|prev",
            "?трек|песню|песня|композицию|track|song"),

        PhrasePattern.Of(IntentKind.MediaPause, Normal,
            "?сделай|поставь|нажми|давай",
            "пауза|паузу|стоп|останови|остановись|приостанови|хватит|тихо|замолчи|заткнись|pause|stop",
            "?музыку|музыка|трек|песню|воспроизведение|играть|это"),

        PhrasePattern.Of(IntentKind.MediaPlay, Normal,
            "?давай|ну",
            "продолжи|продолжай|продолжить|возобнови|играй|отомри|resume|continue|unpause",
            "?музыку|музыка|трек|песню|воспроизведение|играть|дальше"),

        PhrasePattern.Of(IntentKind.PlayMusic, Loose,
            "?включи|поставь|запусти|врубай|вруби|давай|open|play|start",
            "?мне|мою|моя|мой|любимую|последний|последнюю",
            "музыку|музыка|музон|музычку|плейлист|плейлисты|треки|песни|playlist|music|tunes"),

        PhrasePattern.Of(IntentKind.NowPlaying, Normal,
            "?а|и",
            "что|какая|какой|кто|which|what",
            "?сейчас|это|там",
            "играет|поёт|поет|звучит|трек|песня|играло|playing"),

        // ---- Громкость ------------------------------------------------------

        PhrasePattern.Of(IntentKind.VolumeUp, Normal,
            "?сделай|поставь|давай",
            "громче|погромче|прибавь|увеличь|louder",
            "?звук|громкость|музыку|немного"),

        PhrasePattern.Of(IntentKind.VolumeDown, Normal,
            "?сделай|поставь|давай",
            "тише|потише|убавь|уменьши|приглуши|quieter",
            "?звук|громкость|музыку|немного"),

        PhrasePattern.Of(IntentKind.VolumeMute, Normal,
            "?сделай|поставь",
            "выключи|отключи|убери|заглуши|mute",
            "звук|звука|громкость|sound"),

        // ---- Яркость --------------------------------------------------------

        PhrasePattern.Of(IntentKind.BrightnessUp, Normal,
            "?сделай|поставь|давай",
            "ярче|поярче|прибавь|увеличь|brighter",
            "?яркость|экран|немного"),

        PhrasePattern.Of(IntentKind.BrightnessDown, Normal,
            "?сделай|поставь|давай",
            "темнее|потемнее|притуши|убавь|уменьши|dimmer",
            "?яркость|экран|немного"),

        // ---- Окна ------------------------------------------------------------

        PhrasePattern.Of(IntentKind.CloseWindow, Careful,
            "закрой|убери|закрыть|close",
            "окно|окошко|это|программу|window"),

        PhrasePattern.Of(IntentKind.MinimizeWindow, Normal,
            "сверни|свернуть|спрячь|убери|minimize",
            "?окно|это|программу|вниз"),

        PhrasePattern.Of(IntentKind.MaximizeWindow, Normal,
            "разверни|развернуть|раскрой|maximize",
            "?окно|это|на|весь|экран"),

        PhrasePattern.Of(IntentKind.RestoreWindow, Normal,
            "верни|восстанови|уменьши|restore",
            "окно|окошко|window"),

        PhrasePattern.Of(IntentKind.ShowDesktop, Normal,
            "?покажи|сверни|убери",
            "рабочий|все|всё|desktop|свернуть",
            "?стол|окна|окно"),

        // Необязательный слот для «окна» стоит с обеих сторон направления:
        // по-русски одинаково говорят и «прижми окно влево», и «прижми влево
        // окно», а слоты идут по порядку и сами этого не знают.
        PhrasePattern.Of(IntentKind.SnapLeft, Normal,
            "?прижми|подвинь|поставь|отправь",
            "?окно|окошко|это",
            "влево|налево|левее|left",
            "?окно|половину|край"),

        PhrasePattern.Of(IntentKind.SnapRight, Normal,
            "?прижми|подвинь|поставь|отправь",
            "?окно|окошко|это",
            "вправо|направо|правее|right",
            "?окно|половину|край"),

        PhrasePattern.Of(IntentKind.SwitchWindow, Normal,
            "?давай|сделай",
            "переключись|переключи|смени|переключение",
            "?окно|окна|между|window"),

        PhrasePattern.Of(IntentKind.TaskView, Normal,
            "?покажи|открой",
            "все|всё|обзор|задачи|task",
            "окна|окон|задач|view"),

        // ---- Рабочие столы ---------------------------------------------------

        PhrasePattern.Of(IntentKind.NextDesktop, Normal,
            "?перейди|давай|переключись",
            "следующий|следующая|вправо|next",
            "рабочий|стол|desktop",
            "?стол"),

        PhrasePattern.Of(IntentKind.PreviousDesktop, Normal,
            "?перейди|давай|переключись",
            "предыдущий|прошлый|влево|previous",
            "рабочий|стол|desktop",
            "?стол"),

        PhrasePattern.Of(IntentKind.NewDesktop, Normal,
            "?создай|сделай|открой",
            "новый|новая|создай",
            "рабочий|стол|desktop",
            "?стол"),

        // ---- Прокрутка и масштаб ---------------------------------------------

        PhrasePattern.Of(IntentKind.ScrollDown, Loose,
            "?прокрути|пролистай|листай|крути|scroll",
            "вниз|ниже|down"),

        PhrasePattern.Of(IntentKind.ScrollUp, Loose,
            "?прокрути|пролистай|листай|крути|scroll",
            "вверх|выше|наверх|up"),

        PhrasePattern.Of(IntentKind.ScrollTop, Normal,
            "?прокрути|перейди|листай",
            "в|к|на",
            "начало|верх|самый|top",
            "?страницы|верх"),

        PhrasePattern.Of(IntentKind.ScrollBottom, Normal,
            "?прокрути|перейди|листай",
            "в|к|на",
            "конец|низ|самый|bottom",
            "?страницы|низ"),

        PhrasePattern.Of(IntentKind.ZoomIn, Normal,
            "?сделай|давай",
            "приблизь|увеличь|крупнее|покрупнее|zoom",
            "?масштаб|текст|экран|in"),

        PhrasePattern.Of(IntentKind.ZoomOut, Normal,
            "?сделай|давай",
            "отдали|уменьши|мельче|помельче",
            "?масштаб|текст|экран|out"),

        PhrasePattern.Of(IntentKind.ZoomReset, Normal,
            "?сделай|верни",
            "обычный|нормальный|исходный|сбрось",
            "масштаб|размер|zoom"),

        PhrasePattern.Of(IntentKind.FullScreen, Normal,
            "?сделай|включи|давай",
            "полный|полноэкранный|весь|fullscreen",
            "?экран|режим"),

        // ---- Текст ------------------------------------------------------------

        PhrasePattern.Of(IntentKind.Copy, Normal,
            "скопируй|копировать|копируй|copy",
            "?это|текст|выделенное|that"),

        PhrasePattern.Of(IntentKind.Paste, Normal,
            "вставь|вставить|вставляй|paste",
            "?это|текст|сюда|that"),

        PhrasePattern.Of(IntentKind.Cut, Careful,
            "вырежи|вырезать|вырежь|cut",
            "?это|текст|выделенное"),

        PhrasePattern.Of(IntentKind.Undo, Normal,
            "?давай",
            "отмени|отменить|верни|назад|undo",
            "?это|действие|последнее|что|сделал"),

        PhrasePattern.Of(IntentKind.Redo, Normal,
            "?давай",
            "повтори|верни|redo",
            "действие|обратно|отменённое|redo"),

        PhrasePattern.Of(IntentKind.SelectAll, Normal,
            "выдели|выделить|выделяй|select",
            "все|всё|весь|all",
            "?текст"),

        PhrasePattern.Of(IntentKind.Save, Normal,
            "сохрани|сохранить|save",
            "?это|файл|документ|всё"),

        PhrasePattern.Of(IntentKind.Print, Careful,
            "распечатай|печатай|напечатай|print",
            "это|документ|страницу|файл"),

        PhrasePattern.Of(IntentKind.FindOnPage, Normal,
            "?давай",
            "найди|поиск|искать|find",
            "на|по",
            "странице|тексту|page"),

        // ---- Мышь и клавиши ----------------------------------------------------

        PhrasePattern.Of(IntentKind.MouseRightClick, Normal,
            "?сделай|нажми",
            "правый|правой|контекстное",
            "клик|кнопкой|меню|click"),

        PhrasePattern.Of(IntentKind.MouseDoubleClick, Normal,
            "?сделай|нажми",
            "двойной|дважды|double",
            "клик|щелчок|click"),

        PhrasePattern.Of(IntentKind.MouseClick, Normal,
            "?сделай|нажми|давай",
            "клик|кликни|щелчок|щёлкни|click|tap"),

        PhrasePattern.Of(IntentKind.PressEnter, Normal,
            "?нажми|жми|давай",
            "ввод|энтер|enter|return"),

        PhrasePattern.Of(IntentKind.PressEscape, Normal,
            "?нажми|жми|давай",
            "отмена|escape|эскейп|esc"),

        PhrasePattern.Of(IntentKind.PressTab, Normal,
            "?нажми|жми|давай",
            "таб|табуляция|tab"),

        PhrasePattern.Of(IntentKind.PressBackspace, Normal,
            "?нажми|жми|давай",
            "стереть|backspace|бэкспейс|удали",
            "?символ|букву|назад"),

        PhrasePattern.Of(IntentKind.PressDelete, Normal,
            "?нажми|жми|давай",
            "delete|делит|удалить",
            "?символ|это"),

        PhrasePattern.Of(IntentKind.PressUp, Normal,
            "?нажми|жми",
            "стрелка|стрелку",
            "вверх|up"),

        PhrasePattern.Of(IntentKind.PressDown, Normal,
            "?нажми|жми",
            "стрелка|стрелку",
            "вниз|down"),

        PhrasePattern.Of(IntentKind.PressLeft, Normal,
            "?нажми|жми",
            "стрелка|стрелку",
            "влево|left"),

        PhrasePattern.Of(IntentKind.PressRight, Normal,
            "?нажми|жми",
            "стрелка|стрелку",
            "вправо|right"),

        // ---- Браузер -------------------------------------------------------------

        PhrasePattern.Of(IntentKind.BrowserBack, Normal,
            "?давай|шаг|вернись",
            "назад|обратно|back",
            "?страницу|шаг"),

        PhrasePattern.Of(IntentKind.BrowserForward, Normal,
            "?давай|шаг",
            "вперёд|вперед|forward",
            "?страницу|шаг"),

        PhrasePattern.Of(IntentKind.BrowserRefresh, Normal,
            "обнови|обновить|перезагрузи|refresh|reload",
            "?страницу|это|вкладку|page"),

        PhrasePattern.Of(IntentKind.Bookmark, Normal,
            "?добавь|сохрани|сделай",
            "в|это|эту",
            "закладки|закладку|bookmark"),

        PhrasePattern.Of(IntentKind.IncognitoWindow, Normal,
            "?открой|запусти|новое",
            "?окно|новое",
            "инкогнито|приватное|incognito|private",
            "?окно|режим|window"),

        // ---- Места Windows --------------------------------------------------------

        PhrasePattern.Of(IntentKind.OpenExplorer, Normal,
            "?открой|запусти|покажи",
            "проводник|файлы|папки|explorer"),

        PhrasePattern.Of(IntentKind.OpenSettings, Normal,
            "?открой|запусти|покажи",
            "настройки|параметры|settings",
            "?windows|системы|виндовс"),

        PhrasePattern.Of(IntentKind.OpenTaskManager, Normal,
            "?открой|запусти|покажи",
            "диспетчер",
            "задач|задачи|процессов"),

        PhrasePattern.Of(IntentKind.OpenNotifications, Normal,
            "?открой|покажи",
            "уведомления|центр|notifications",
            "?уведомлений"),

        PhrasePattern.Of(IntentKind.OpenClipboard, Normal,
            "?открой|покажи",
            "буфер|историю|clipboard",
            "обмена|буфера|копирований"),

        PhrasePattern.Of(IntentKind.OpenEmoji, Normal,
            "?открой|покажи|давай",
            "эмодзи|смайлы|смайлики|emoji"),

        PhrasePattern.Of(IntentKind.OpenStartMenu, Normal,
            "?открой|покажи|нажми",
            "пуск|меню|start"),

        PhrasePattern.Of(IntentKind.OpenSearch, Normal,
            "?открой|покажи",
            "поиск|найти",
            "windows|виндовс|системе|компьютере"),

        PhrasePattern.Of(IntentKind.OpenRun, Normal,
            "?открой|покажи",
            "выполнить|run",
            "?окно|команду"),

        // ---- Система ---------------------------------------------------------------

        PhrasePattern.Of(IntentKind.LockWorkstation, Careful,
            "заблокируй|заблокировать|блокировка|запри|lock",
            "?компьютер|экран|комп|это"),

        PhrasePattern.Of(IntentKind.Sleep, Careful,
            "?переведи|уйди|отправь",
            "спящий|сон|спать|sleep",
            "?режим|компьютер"),

        PhrasePattern.Of(IntentKind.Screenshot, Normal,
            "?сделай|давай|сними",
            "скриншот|снимок|скрин|screenshot",
            "?экрана|экран"),

        PhrasePattern.Of(IntentKind.Time, Normal,
            "?скажи|а",
            "который|сколько|текущее|what",
            "час|времени|время|time"),

        PhrasePattern.Of(IntentKind.Date, Normal,
            "?скажи|а",
            "какое|какая|текущая|сегодня|what",
            "?сегодня|сейчас",
            "число|дата|день|date",
            "?сегодня"),

        PhrasePattern.Of(IntentKind.Battery, Normal,
            "?скажи|сколько|а",
            "заряд|батарея|батареи|аккумулятор|battery",
            "?осталось|процентов"),

        PhrasePattern.Of(IntentKind.CancelTimers, Normal,
            "отмени|убери|сбрось|останови",
            "таймер|таймеры|напоминание|напоминания|будильник"),
    };
}
