using System.Net;
using Hika.Catalog;
using Hika.Config;
using Hika.Diagnostics;
using Hika.Interop;
using Hika.Nlu;

namespace Hika.Skills;

/// <summary>Направляет разобранное намерение в исполнителя.</summary>
public sealed class SkillRouter
{
    private readonly AppCatalog _catalog;
    private readonly Timers _timers;
    private readonly MusicSkill _music;

    public SkillRouter(AppCatalog catalog, Timers? timers = null)
    {
        _catalog = catalog;
        _timers = timers ?? new Timers();
        _music = new MusicSkill(catalog);
    }

    public Timers Timers => _timers;

    public SkillResult Execute(Intent intent, BehaviorConfig behavior)
    {
        try
        {
            return intent.Kind switch
            {
                IntentKind.Launch => Launch(intent.Argument, behavior, intent.ExplicitVerb),
                IntentKind.Search => Search(intent.Argument, behavior),
                IntentKind.FocusWindow => Focus(intent.Argument, behavior),

                IntentKind.VolumeUp => SystemActions.VolumeUp(),
                IntentKind.VolumeDown => SystemActions.VolumeDown(),
                IntentKind.VolumeMute => SystemActions.ToggleMute(),
                IntentKind.VolumeSet => SystemActions.SetVolume(Number(intent.Argument, 50)),

                // Всё, что касается воспроизведения, идёт через список сеансов
                // Windows: там видно, кто именно играет, и пауза достаётся ему,
                // а не уходит наугад мультимедийной клавишей.
                IntentKind.MediaPlayPause => MediaSessions.PlayPause(),
                IntentKind.MediaPause => MediaSessions.Pause(),
                IntentKind.MediaPlay => MediaSessions.Play(),
                IntentKind.MediaNext => MediaSessions.Next(),
                IntentKind.MediaPrevious => MediaSessions.Previous(),
                IntentKind.NowPlaying => MediaSessions.NowPlaying(),
                IntentKind.PlayMusic => _music.Play(behavior),

                IntentKind.LockWorkstation => SystemActions.LockWorkstation(),
                IntentKind.ShowDesktop => SystemActions.ShowDesktop(),
                IntentKind.MinimizeWindow => SystemActions.MinimizeActiveWindow(),
                IntentKind.CloseWindow => SystemActions.CloseActiveWindow(),
                IntentKind.Screenshot => SystemActions.Screenshot(),
                IntentKind.Sleep => SystemActions.Sleep(),
                IntentKind.Time => SystemActions.Time(),

                IntentKind.Timer => _timers.Start(TimeSpan.FromSeconds(Number(intent.Argument, 300))),

                IntentKind.NewTab => SystemActions.Combo("новая вкладка", Win32.VK_CONTROL, Win32.VK_T),
                IntentKind.CloseTab => SystemActions.Combo("вкладка закрыта", Win32.VK_CONTROL, Win32.VK_W),
                IntentKind.BrowserBack => SystemActions.Combo("шаг назад", Win32.VK_MENU, Win32.VK_LEFT),
                IntentKind.BrowserRefresh => SystemActions.Combo("страница обновлена", Win32.VK_F5),

                IntentKind.NextDesktop => SystemActions.Combo("следующий рабочий стол",
                    Win32.VK_LWIN, Win32.VK_CONTROL, Win32.VK_RIGHT),
                IntentKind.PreviousDesktop => SystemActions.Combo("предыдущий рабочий стол",
                    Win32.VK_LWIN, Win32.VK_CONTROL, Win32.VK_LEFT),

                _ => SkillResult.Fail("команда не распознана"),
            };
        }
        catch (Exception ex)
        {
            Log.Error($"исполнение намерения {intent} сорвалось", ex, "skills");
            return SkillResult.Fail("внутренняя ошибка");
        }
    }

    private static int Number(string argument, int fallback)
        => int.TryParse(argument, out var value) ? value : fallback;

    /// <summary>
    /// Переключение на открытое окно, а если такого нет — обычный запуск.
    ///
    /// Второе важно не меньше первого: «переключись на хром» при закрытом
    /// браузере значит «пусть он будет передо мной», и отвечать на это
    /// отказом было бы формализмом.
    /// </summary>
    private SkillResult Focus(string phrase, BehaviorConfig behavior)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return SkillResult.Fail("не понял, на что переключиться");

        var focused = WindowSwitcher.TryFocus(phrase);
        if (focused is not null) return focused;

        Log.Info($"окна «{phrase}» нет — пробую запустить", "skills");
        return Launch(phrase, behavior, explicitVerb: true);
    }

    private SkillResult Launch(string phrase, BehaviorConfig behavior, bool explicitVerb)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return SkillResult.Fail("нечего открывать");

        // Явный глагол снимает неоднозначность намерения, и на это стоит
        // опереться: раз человек сказал «запусти», он говорит о программе.
        // Значит, можно смелее искать по каталогу — пропустить установленную
        // игру с непривычным названием хуже, чем разок открыть не то.
        var threshold = explicitVerb ? behavior.MatchThreshold * 0.78 : behavior.MatchThreshold;

        var match = _catalog.Resolve(phrase, threshold);
        if (match is not null) return Launcher.Launch(match.Entry).From(match.Entry);

        // Похоже на адрес сайта — открываем как есть.
        if (LooksLikeDomain(phrase))
            return Launcher.OpenUrl(phrase.Replace(" ", ""), phrase);

        // И вот здесь глагол решает исход. «Запусти Helldivers 2» — просьба
        // запустить игру; открыть вместо неё поисковую выдачу значит сделать
        // не то, о чём просили. Честнее признать, что не нашли.
        if (explicitVerb)
        {
            Log.Info($"«{phrase}» не найдено, но глагол был явный — в поиск не ухожу", "skills");
            return SkillResult.Fail($"«{phrase}» не найдено среди установленных программ");
        }

        // Цель названа без глагола, намерение неочевидно. Молча проглотить
        // команду хуже, чем показать результаты: человек хотя бы увидит,
        // что его услышали.
        if (behavior.WebSearchFallback)
        {
            Log.Info($"«{phrase}» в каталоге нет, ухожу в поиск", "skills");
            return Search(phrase, behavior);
        }

        return SkillResult.Fail($"«{phrase}» не найдено");
    }

    private static SkillResult Search(string query, BehaviorConfig behavior)
    {
        if (string.IsNullOrWhiteSpace(query)) return SkillResult.Fail("пустой запрос");

        var template = string.IsNullOrWhiteSpace(behavior.SearchUrl)
            ? "https://www.google.com/search?q={q}"
            : behavior.SearchUrl;

        var url = template.Replace("{q}", WebUtility.UrlEncode(query));
        return Launcher.OpenUrl(url, $"поиск «{query}»");
    }

    private static bool LooksLikeDomain(string phrase)
    {
        var compact = phrase.Replace(" ", "");
        if (!compact.Contains('.')) return false;
        if (compact.Length < 4 || compact.Length > 100) return false;

        var lastDot = compact.LastIndexOf('.');
        var tld = compact[(lastDot + 1)..];

        return tld.Length is >= 2 and <= 6 && tld.All(char.IsLetter);
    }
}
