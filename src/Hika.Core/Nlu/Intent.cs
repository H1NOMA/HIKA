namespace Hika.Nlu;

public enum IntentKind
{
    None,

    /// <summary>Открыть программу или сайт. Аргумент — то, что нужно открыть.</summary>
    Launch,

    /// <summary>Поискать в интернете. Аргумент — запрос.</summary>
    Search,

    VolumeUp,
    VolumeDown,
    VolumeMute,

    /// <summary>Громкость на столько-то процентов. Аргумент — число.</summary>
    VolumeSet,

    MediaPlayPause,

    /// <summary>Именно остановить, а не переключить: «пауза», «стоп».</summary>
    MediaPause,

    /// <summary>Именно продолжить: «продолжи», «включи обратно».</summary>
    MediaPlay,

    MediaNext,
    MediaPrevious,

    /// <summary>Сказать, что играет.</summary>
    NowPlaying,

    /// <summary>Включить музыку: продолжить приостановленное или поднять плеер.</summary>
    PlayMusic,

    LockWorkstation,
    ShowDesktop,
    MinimizeWindow,
    CloseWindow,
    Screenshot,

    /// <summary>Перевести компьютер в сон.</summary>
    Sleep,

    /// <summary>Переключиться на окно. Аргумент — часть его заголовка.</summary>
    FocusWindow,

    /// <summary>Сказать время.</summary>
    Time,

    /// <summary>Напомнить через промежуток. Аргумент — секунды.</summary>
    Timer,

    NewTab,
    CloseTab,
    BrowserBack,
    BrowserRefresh,

    NextDesktop,
    PreviousDesktop,
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
