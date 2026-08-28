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
    /// Плееры, у которых нечего перематывать стрелками: там звук, а не видео.
    ///
    /// Список нужен ровно для того, чтобы не поднимать их окно поверх работы
    /// человека. Команда «перемотай» при играющей музыке — почти всегда
    /// оговорка или обращение к видео, которого сейчас нет.
    /// </summary>
    private static readonly string[] AudioOnly =
    {
        "Spotify", "Apple Music", "iTunes", "AIMP", "foobar2000", "Winamp",
        "Яндекс.Музыка", "Яндекс Музыка", "Музыка", "Медиаплеер",
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

            var count = Math.Max(1, times);

            for (int i = 0; i < count; i++)
            {
                Win32.TapCombo(keys);

                // Пауза между нажатиями, но не после последнего: она нужна,
                // чтобы плеер успел разобрать предыдущее, а после последнего
                // разбирать уже нечего — это чистое ожидание человека.
                if (i + 1 < count) Thread.Sleep(35);
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

        // Играет музыка, а не видео. Поднимать её окно ради стрелки
        // бессмысленно вдвойне: перемотки в музыкальных плеерах на стрелках
        // нет, зато человек, который в это время печатал, лишится того, что
        // печатал. Отказ здесь полезнее попытки.
        if (AudioOnly.Any(a => hint.Contains(a, StringComparison.OrdinalIgnoreCase)))
        {
            Log.Info($"играет {hint} — это музыка, а не видео; фокус не трогаю", "media");
            return null;
        }

        var focused = WindowSwitcher.TryFocus(hint);

        if (focused is null || !focused.Success)
        {
            Log.Debug($"«{hint}» играет, но поднять его окно не вышло", "media");
            return null;
        }

        // Окну нужно мгновение, чтобы начать принимать ввод.
        Thread.Sleep(120);
        return hint;
    }

    /// <summary>
    /// Впереди плеер или браузер.
    ///
    /// Сравнение по началу имени, а не по вхождению куска: «obs» встречается
    /// внутри «Obsidian», «media» — внутри доброго десятка программ, и клавиша
    /// «f» уезжала бы в заметки вместо плеера.
    /// </summary>
    private static bool IsPlayer(string process)
    {
        if (process.Length == 0) return false;

        return Players.Any(p =>
            process.Equals(p, StringComparison.OrdinalIgnoreCase) ||
            process.StartsWith(p, StringComparison.OrdinalIgnoreCase));
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
