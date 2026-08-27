namespace Hika.Nlu;

/// <summary>
/// Что человек хотел.
///
/// Набор выстроен по тем же разделам, по которым устроены голосовое управление
/// Windows и известные ассистенты: приложения и окна, экран и прокрутка, мышь
/// и клавиши, текст, система, браузер, воспроизведение. Разделы не выдуманы —
/// они повторяются у всех, потому что отражают не устройство программы,
/// а то, из чего состоит работа за компьютером.
/// </summary>
public enum IntentKind
{
    None,

    // ---- Приложения и окна ------------------------------------------------

    /// <summary>Открыть программу или сайт. Аргумент — то, что нужно открыть.</summary>
    Launch,

    /// <summary>Поискать в интернете. Аргумент — запрос.</summary>
    Search,

    /// <summary>Переключиться на открытое окно. Аргумент — часть его заголовка.</summary>
    FocusWindow,

    MinimizeWindow,
    MaximizeWindow,
    RestoreWindow,
    CloseWindow,
    ShowDesktop,

    /// <summary>Прижать окно к левой или правой половине экрана.</summary>
    SnapLeft,
    SnapRight,

    /// <summary>Переключение между окнами и обзор задач.</summary>
    SwitchWindow,
    TaskView,

    NextDesktop,
    PreviousDesktop,
    NewDesktop,
    CloseDesktop,

    // ---- Экран и прокрутка -------------------------------------------------

    ScrollUp,
    ScrollDown,
    ScrollTop,
    ScrollBottom,

    ZoomIn,
    ZoomOut,
    ZoomReset,
    FullScreen,

    // ---- Мышь и клавиши ----------------------------------------------------

    MouseClick,
    MouseRightClick,
    MouseDoubleClick,

    PressEnter,
    PressEscape,
    PressTab,
    PressBackspace,
    PressDelete,
    PressUp,
    PressDown,
    PressLeft,
    PressRight,

    // ---- Текст -------------------------------------------------------------

    Copy,
    Paste,
    Cut,
    Undo,
    Redo,
    SelectAll,
    Save,
    Print,
    FindOnPage,

    /// <summary>Набрать текст. Аргумент — что именно печатать.</summary>
    TypeText,

    // ---- Звук и воспроизведение -------------------------------------------

    VolumeUp,
    VolumeDown,
    VolumeMute,

    /// <summary>Громкость на столько-то процентов. Аргумент — число.</summary>
    VolumeSet,

    MediaPlayPause,

    /// <summary>Именно остановить, а не переключить: «пауза», «стоп».</summary>
    MediaPause,

    /// <summary>Именно продолжить: «продолжи», «возобнови».</summary>
    MediaPlay,

    MediaNext,
    MediaPrevious,

    /// <summary>Сказать, что играет.</summary>
    NowPlaying,

    /// <summary>Включить музыку: продолжить приостановленное или поднять плеер.</summary>
    PlayMusic,

    // ---- Видео: ютуб и любой плеер -----------------------------------------
    //
    // Всё здесь делается клавишами, которые плеер разбирает сам. Список сеансов
    // Windows про перемотку, субтитры и скорость не знает вовсе — он умеет
    // только «играй», «стой», «дальше», — а именно этих трёх команд человеку
    // и не хватает, когда он смотрит видео.

    /// <summary>Перемотать немного вперёд.</summary>
    MediaSeekForward,

    /// <summary>Перемотать немного назад.</summary>
    MediaSeekBackward,

    /// <summary>Перемотать заметно вперёд — «на минуту», «подальше».</summary>
    MediaSeekForwardFar,

    /// <summary>Перемотать заметно назад.</summary>
    MediaSeekBackwardFar,

    /// <summary>В начало: «сначала», «заново».</summary>
    MediaRestart,

    /// <summary>Видео на весь экран.</summary>
    MediaFullScreen,

    /// <summary>Широкий (театральный) режим ютуба.</summary>
    MediaTheater,

    /// <summary>Мини-плеер в углу.</summary>
    MediaMiniPlayer,

    /// <summary>Субтитры.</summary>
    MediaCaptions,

    MediaSpeedUp,
    MediaSpeedDown,

    /// <summary>Заглушить сам плеер, не трогая громкость системы.</summary>
    MediaMute,

    NextVideo,
    PreviousVideo,

    // ---- Диктовка ------------------------------------------------------------

    /// <summary>Всё сказанное дальше набирать в активное окно.</summary>
    DictationStart,

    /// <summary>Закончить диктовку.</summary>
    DictationStop,

    // ---- Система -----------------------------------------------------------

    LockWorkstation,
    Screenshot,

    /// <summary>Перевести компьютер в сон.</summary>
    Sleep,

    BrightnessUp,
    BrightnessDown,

    /// <summary>Яркость на столько-то процентов. Аргумент — число.</summary>
    BrightnessSet,

    /// <summary>Сказать время.</summary>
    Time,

    /// <summary>Сказать дату.</summary>
    Date,

    /// <summary>Сказать заряд батареи.</summary>
    Battery,

    /// <summary>Напомнить через промежуток. Аргумент — секунды.</summary>
    Timer,

    /// <summary>Отменить идущие таймеры.</summary>
    CancelTimers,

    // ---- Места Windows -----------------------------------------------------

    OpenStartMenu,
    OpenSearch,
    OpenSettings,
    OpenExplorer,
    OpenTaskManager,
    OpenNotifications,
    OpenClipboard,
    OpenEmoji,
    OpenRun,

    // ---- Браузер -----------------------------------------------------------

    NewTab,
    CloseTab,
    ReopenTab,
    NextTab,
    PreviousTab,
    BrowserBack,
    BrowserForward,
    BrowserRefresh,
    Bookmark,
    IncognitoWindow,
}

public sealed record Intent(IntentKind Kind, string Argument = "", double Confidence = 1.0)
{
    public static readonly Intent None = new(IntentKind.None);

    /// <summary>
    /// Человек произнёс явный глагол запуска: «запусти», «открой», «включи».
    ///
    /// Это меняет всё дальнейшее поведение. Сказавший «запусти Helldivers 2»
    /// хочет запустить игру, а не почитать про неё в интернете — и если такая
    /// команда не нашлась в каталоге, правильно признать неудачу, а не открыть
    /// браузер с поисковой выдачей. Уход в поиск уместен, только когда цель
    /// названа без глагола и намерение неочевидно.
    /// </summary>
    public bool ExplicitVerb { get; init; }

    public bool IsActionable => Kind != IntentKind.None;

    public override string ToString()
        => string.IsNullOrEmpty(Argument) ? Kind.ToString() : $"{Kind}(«{Argument}»)";
}
