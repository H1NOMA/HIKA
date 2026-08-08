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

    MediaPlayPause,
    MediaNext,
    MediaPrevious,

    LockWorkstation,
    ShowDesktop,
    MinimizeWindow,
    CloseWindow,
    Screenshot,
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
