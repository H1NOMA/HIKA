using System.Diagnostics;
using Hika.Diagnostics;
using Hika.Interop;

namespace Hika.Skills;

/// <summary>
/// Клавиши плееру: ютубу, VLC, твичу — всему, что показывает видео.
///
/// Перемотка, субтитры, скорость и полный экран не выражаются через список
/// сеансов Windows: он умеет ровно три вещи — играй, стой, дальше. Всё
/// остальное плееры делают клавишами, и общих клавиш у них больше, чем
/// кажется: стрелки перематывают и в ютубе, и в VLC, и в любом видео
/// на странице; F разворачивает, M глушит, C включает субтитры.
///
/// Отсюда и главная особенность: клавиша достаётся тому окну, которое сейчас
/// впереди. Поэтому она отправляется не вслепую.
///
/// Если впереди плеер или браузер — всё просто. Если нет, спрашиваем систему,
/// кто вообще играет, и поднимаем его окно: человек просил перемотать видео,
/// а не напечатать букву «f» в переписку. А если не играет никто — честно
/// отвечаем, что плеера не видно. Это тот случай, когда молча сделать
/// не то хуже, чем не сделать ничего.
/// </summary>
public static class PlayerKeys
{
    /// <summary>
    /// Кому можно отправлять клавиши. Браузеры здесь потому, что видео
    /// сегодня чаще всего смотрят в них.
    /// </summary>
    private static readonly string[] Players =
    {
        "chrome", "msedge", "edge", "firefox", "opera", "browser", "vivaldi", "brave",
        "yandex", "safari", "iexplore",
        "vlc", "mpc-hc", "mpc-be", "mpv", "potplayer", "kmplayer", "gom", "media",
        "wmplayer", "aimp", "foobar2000", "winamp", "spotify", "applemusic", "itunes",
        "music", "video", "movies", "zune", "plex", "kodi", "twitch", "discord",
        "telegram", "steam", "obs",
    };

    /// <summary>
    /// Отправляет сочетание тому, кто показывает видео.
    /// </summary>
    public static SkillResult Send(string description, params ushort[] keys)
        => Send(description, 1, keys);

    /// <summary>
    /// То же, но нажатие повторяется несколько раз.
    ///
    /// Нужно для «перемотай на минуту»: единой клавиши «прыгнуть на минуту»
    /// нет ни у кого, зато стрелка есть у всех. Шесть нажатий — это полминуты
    /// в ютубе и около минуты в VLC; точнее без знания плеера не выйдет,
    /// а человек, просящий «подальше», точности и не ждёт.
    /// </summary>
    public static SkillResult Send(string description, int times, params ushort[] keys)
    {
        try
        {
            var target = Target();
            if (target is null) return SkillResult.Fail("не вижу плеера — откройте видео и повторите");

            for (int i = 0; i < Math.Max(1, times); i++)
            {
                Win32.TapCombo(keys);
                if (times > 1) Thread.Sleep(35);
            }

            Log.Info($"{description} -> {target}", "media");
            return SkillResult.Ok(description);
        }
        catch (Exception ex)
        {
            Log.Error($"клавиша плееру не отправилась: {description}", ex, "media");
            return SkillResult.Fail("не вышло");
        }
    }

    /// <summary>
    /// Кому достанется клавиша. Возвращает имя окна или null, если плеера
    /// не нашлось вовсе.
    /// </summary>
    private static string? Target()
    {
        var foreground = ProcessName(Win32.GetForegroundWindow());

        if (IsPlayer(foreground)) return foreground;

        // Впереди не плеер. Но кто-то ведь играет — список сеансов Windows
        // знает, кто именно, и его окно можно поднять.
        var hint = MediaSessions.PlayingAppHint();
        if (hint.Length == 0)
        {
            Log.Debug($"впереди {(foreground.Length == 0 ? "неизвестно что" : foreground)}, " +
                      "и ничего не играет — клавишу отправлять некому", "media");
            return null;
        }

        if (WindowSwitcher.TryFocus(hint) is null)
        {
            Log.Debug($"«{hint}» играет, но его окна не видно", "media");
            return null;
        }

        // Окну нужно мгновение, чтобы стать активным и начать принимать ввод.
        Thread.Sleep(140);
        return hint;
    }

    private static bool IsPlayer(string process)
    {
        if (process.Length == 0) return false;
        return Players.Any(p => process.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    private static string ProcessName(IntPtr window)
    {
        if (window == IntPtr.Zero) return "";

        try
        {
            Win32.GetWindowThreadProcessId(window, out var pid);
            if (pid == 0) return "";

            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return "";
        }
    }
}
