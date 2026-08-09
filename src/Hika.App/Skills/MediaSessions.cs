using Hika.Diagnostics;
using Hika.Interop;
using Windows.Media.Control;

namespace Hika.Skills;

/// <summary>
/// Управление тем, что играет прямо сейчас.
///
/// Раньше «пауза» была просто нажатием мультимедийной клавиши. Способ рабочий
/// и всеобщий, но слепой: клавиша уходит в никуда, и узнать, дошла ли она
/// хоть до кого-нибудь, нельзя. Хуже другое — адресат выбирает Windows,
/// и выбирает она не всегда того, кого имел в виду человек: при открытом
/// ютубе и свёрнутом плеере пауза уходит наугад.
///
/// Windows ведёт список сеансов воспроизведения — тот самый, что показывается
/// в панели громкости. В нём видно, кто именно играет, что играет и можно ли
/// им управлять. Отсюда всё и берётся: пауза адресуется тому, кто действительно
/// звучит, а в ответ появляется имя приложения и название трека.
///
/// Клавиша остаётся запасным путём: список сеансов ведут не все плееры,
/// а старые программы не ведут вовсе.
/// </summary>
public static class MediaSessions
{
    /// <summary>Сколько ждать систему. Дольше секунды — уже заметная пауза перед действием.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1200);

    public static SkillResult PlayPause()
    {
        var playing = Find(s => Status(s) == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
        if (playing is not null) return Command(playing, s => s.TryPauseAsync(), "пауза");

        var paused = Find(s => Status(s) == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused);
        if (paused is not null) return Command(paused, s => s.TryPlayAsync(), "продолжаю");

        return Fallback(Win32.VK_MEDIA_PLAY_PAUSE, "воспроизведение переключено");
    }

    public static SkillResult Pause()
    {
        var playing = Find(s => Status(s) == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
        if (playing is not null) return Command(playing, s => s.TryPauseAsync(), "пауза");

        // Уже стоит на паузе — это не неудача. Человек попросил тишины
        // и получил тишину; сообщать об ошибке тут не о чем.
        if (Find(s => Status(s) == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused) is not null)
            return SkillResult.Ok("уже на паузе");

        return Fallback(Win32.VK_MEDIA_PLAY_PAUSE, "пауза");
    }

    public static SkillResult Play()
    {
        var paused = Find(s => Status(s) == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused);
        if (paused is not null) return Command(paused, s => s.TryPlayAsync(), "продолжаю");

        if (Find(s => Status(s) == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) is not null)
            return SkillResult.Ok("уже играет");

        return Fallback(Win32.VK_MEDIA_PLAY_PAUSE, "воспроизведение");
    }

    public static SkillResult Next()
    {
        var session = Active();
        return session is not null
            ? Command(session, s => s.TrySkipNextAsync(), "следующий трек")
            : Fallback(Win32.VK_MEDIA_NEXT_TRACK, "следующий трек");
    }

    public static SkillResult Previous()
    {
        var session = Active();
        return session is not null
            ? Command(session, s => s.TrySkipPreviousAsync(), "предыдущий трек")
            : Fallback(Win32.VK_MEDIA_PREV_TRACK, "предыдущий трек");
    }

    /// <summary>Что звучит сейчас — фразой, пригодной для произнесения вслух.</summary>
    public static SkillResult NowPlaying()
    {
        var session = Find(s => Status(s) == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                   ?? Active();

        if (session is null) return SkillResult.Fail("сейчас ничего не играет");

        var track = Track(session);
        var app = AppName(session);

        if (string.IsNullOrWhiteSpace(track))
            return SkillResult.Ok(string.IsNullOrWhiteSpace(app) ? "что-то играет" : $"играет {app}");

        return SkillResult.Ok(string.IsNullOrWhiteSpace(app) ? track : $"{track} — {app}");
    }

    /// <summary>Что-нибудь сейчас звучит.</summary>
    public static bool IsPlaying()
        => Find(s => Status(s) == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) is not null;

    /// <summary>Есть приостановленный сеанс — значит, есть что продолжить.</summary>
    public static bool HasPausedSession()
        => Find(s => Status(s) == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused) is not null;

    /// <summary>Плеер объявился в системе — с ним уже можно разговаривать.</summary>
    public static bool HasAnySession()
        => Find(s => Status(s) != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed) is not null;

    // ---- Внутреннее ---------------------------------------------------------

    private static GlobalSystemMediaTransportControlsSessionManager? Manager()
    {
        try
        {
            var task = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
            return task.Wait(Timeout) ? task.Result : null;
        }
        catch (Exception ex)
        {
            Log.Debug($"список сеансов воспроизведения недоступен: {ex.Message}", "media");
            return null;
        }
    }

    /// <summary>Сеанс, на который смотрит система, — обычно последний активный.</summary>
    private static GlobalSystemMediaTransportControlsSession? Active()
    {
        try { return Manager()?.GetCurrentSession(); }
        catch { return null; }
    }

    /// <summary>
    /// Первый подходящий сеанс.
    ///
    /// Порядок обхода начинается с текущего: если играют двое, человек почти
    /// наверняка имел в виду того, с кем работал последним.
    /// </summary>
    private static GlobalSystemMediaTransportControlsSession? Find(
        Func<GlobalSystemMediaTransportControlsSession, bool> predicate)
    {
        try
        {
            var manager = Manager();
            if (manager is null) return null;

            var current = manager.GetCurrentSession();
            if (current is not null && predicate(current)) return current;

            foreach (var session in manager.GetSessions())
            {
                if (predicate(session)) return session;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"обход сеансов воспроизведения сорвался: {ex.Message}", "media");
        }

        return null;
    }

    private static GlobalSystemMediaTransportControlsSessionPlaybackStatus Status(
        GlobalSystemMediaTransportControlsSession session)
    {
        try { return session.GetPlaybackInfo().PlaybackStatus; }
        catch { return GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed; }
    }

    private static SkillResult Command(
        GlobalSystemMediaTransportControlsSession session,
        Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> action,
        string description)
    {
        try
        {
            var task = action(session).AsTask();
            var ok = task.Wait(Timeout) && task.Result;

            var app = AppName(session);
            var full = string.IsNullOrWhiteSpace(app) ? description : $"{description} — {app}";

            if (!ok)
            {
                // Сеанс есть, но команду не принял — такое бывает у вкладок,
                // потерявших связь со страницей. Клавиша ещё может сработать.
                Log.Debug($"{app} не принял команду «{description}», пробую клавишу", "media");
                return Fallback(Win32.VK_MEDIA_PLAY_PAUSE, description);
            }

            Log.Info(full, "media");
            return SkillResult.Ok(full);
        }
        catch (Exception ex)
        {
            Log.Warn($"команда воспроизведения не прошла ({ex.Message}), пробую клавишу", "media");
            return Fallback(Win32.VK_MEDIA_PLAY_PAUSE, description);
        }
    }

    private static SkillResult Fallback(ushort key, string description)
    {
        try
        {
            Win32.TapKey(key);
            Log.Info($"{description} (мультимедийной клавишей)", "media");
            return SkillResult.Ok(description);
        }
        catch (Exception ex)
        {
            Log.Error("медиаклавиша не отправилась", ex, "media");
            return SkillResult.Fail("плеер не найден");
        }
    }

    private static string Track(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var task = session.TryGetMediaPropertiesAsync().AsTask();
            if (!task.Wait(Timeout)) return "";

            var properties = task.Result;
            var title = properties.Title?.Trim() ?? "";
            var artist = properties.Artist?.Trim() ?? "";

            if (title.Length == 0) return artist;
            return artist.Length == 0 ? title : $"{artist} — {title}";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Читаемое имя приложения из его системного идентификатора.</summary>
    private static string AppName(GlobalSystemMediaTransportControlsSession session)
    {
        string id;
        try { id = session.SourceAppUserModelId ?? ""; }
        catch { return ""; }

        if (id.Length == 0) return "";

        // Идентификаторы приложений из магазина — нечитаемые наборы букв,
        // и показывать их человеку незачем.
        foreach (var (needle, name) in KnownApps)
        {
            if (id.Contains(needle, StringComparison.OrdinalIgnoreCase)) return name;
        }

        if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) id = id[..^4];
        if (id.Contains('!') || id.Length > 24) return "";

        return char.ToUpperInvariant(id[0]) + id[1..];
    }

    private static readonly (string Needle, string Name)[] KnownApps =
    {
        ("spotify", "Spotify"),
        ("chrome", "Chrome"),
        ("msedge", "Edge"),
        ("firefox", "Firefox"),
        ("308046B0AF4A39CB", "Firefox"),
        ("yandex", "Яндекс.Браузер"),
        ("opera", "Opera"),
        ("vlc", "VLC"),
        ("aimp", "AIMP"),
        ("foobar", "foobar2000"),
        ("AppleInc.AppleMusic", "Apple Music"),
        ("applemusic", "Apple Music"),
        ("itunes", "iTunes"),
        ("ZuneMusic", "Медиаплеер"),
        ("Microsoft.Media.Player", "Медиаплеер"),
        ("Telegram", "Telegram"),
        ("Discord", "Discord"),
        ("vk.com", "ВКонтакте"),
    };
}
