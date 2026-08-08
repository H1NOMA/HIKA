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

    public bool IsActionable => Kind != IntentKind.None;

    public override string ToString()
        => string.IsNullOrEmpty(Argument) ? Kind.ToString() : $"{Kind}(«{Argument}»)";
}
