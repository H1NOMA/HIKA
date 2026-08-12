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

                // ---- Видео ---------------------------------------------------
                //
                // Здесь список сеансов уже не помощник: он знает только «играй»,
                // «стой» и «дальше». Перемотка, субтитры, скорость и полный
                // экран делаются клавишами, которые плеер разбирает сам, —
                // и уходят они тому окну, которое показывает видео.
                IntentKind.MediaSeekForward => PlayerKeys.Send("перемотала вперёд", Win32.VK_RIGHT),
                IntentKind.MediaSeekBackward => PlayerKeys.Send("перемотала назад", Win32.VK_LEFT),
                IntentKind.MediaSeekForwardFar => PlayerKeys.Send("перемотала вперёд", 6, Win32.VK_RIGHT),
                IntentKind.MediaSeekBackwardFar => PlayerKeys.Send("перемотала назад", 6, Win32.VK_LEFT),
                IntentKind.MediaRestart => PlayerKeys.Send("сначала", Win32.VK_0),
                IntentKind.MediaFullScreen => PlayerKeys.Send("во весь экран", Win32.VK_F),
                IntentKind.MediaTheater => PlayerKeys.Send("широкий режим", Win32.VK_T),
                IntentKind.MediaMiniPlayer => PlayerKeys.Send("мини-плеер", Win32.VK_I),
                IntentKind.MediaCaptions => PlayerKeys.Send("субтитры", Win32.VK_C),
                IntentKind.MediaSpeedUp => PlayerKeys.Send("быстрее", Win32.VK_SHIFT, Win32.VK_OEM_PERIOD),
                IntentKind.MediaSpeedDown => PlayerKeys.Send("медленнее", Win32.VK_SHIFT, Win32.VK_OEM_COMMA),
                IntentKind.MediaMute => PlayerKeys.Send("звук плеера переключён", Win32.VK_M),
                IntentKind.NextVideo => PlayerKeys.Send("следующее видео", Win32.VK_SHIFT, Win32.VK_N),
                IntentKind.PreviousVideo => PlayerKeys.Send("предыдущее видео", Win32.VK_SHIFT, Win32.VK_P),

                // ---- Окна ---------------------------------------------------
                IntentKind.MinimizeWindow => SystemActions.MinimizeActiveWindow(),
                IntentKind.CloseWindow => SystemActions.CloseActiveWindow(),
                IntentKind.ShowDesktop => SystemActions.ShowDesktop(),
                IntentKind.MaximizeWindow => SystemActions.Combo("окно развёрнуто", Win32.VK_LWIN, Win32.VK_UP),
                IntentKind.RestoreWindow => SystemActions.Combo("окно восстановлено", Win32.VK_LWIN, Win32.VK_DOWN),
                IntentKind.SnapLeft => SystemActions.Combo("окно слева", Win32.VK_LWIN, Win32.VK_LEFT),
                IntentKind.SnapRight => SystemActions.Combo("окно справа", Win32.VK_LWIN, Win32.VK_RIGHT),
                IntentKind.SwitchWindow => SystemActions.Combo("переключение окон", Win32.VK_MENU, Win32.VK_TAB),
                IntentKind.TaskView => SystemActions.Combo("обзор задач", Win32.VK_LWIN, Win32.VK_TAB),

                IntentKind.NextDesktop => SystemActions.Combo("следующий рабочий стол",
                    Win32.VK_LWIN, Win32.VK_CONTROL, Win32.VK_RIGHT),
                IntentKind.PreviousDesktop => SystemActions.Combo("предыдущий рабочий стол",
                    Win32.VK_LWIN, Win32.VK_CONTROL, Win32.VK_LEFT),
                IntentKind.NewDesktop => SystemActions.Combo("новый рабочий стол",
                    Win32.VK_LWIN, Win32.VK_CONTROL, Win32.VK_D),
                IntentKind.CloseDesktop => SystemActions.Combo("рабочий стол закрыт",
                    Win32.VK_LWIN, Win32.VK_CONTROL, Win32.VK_F4),

                // ---- Прокрутка и масштаб -------------------------------------
                IntentKind.ScrollUp => SystemActions.Combo("вверх", Win32.VK_PRIOR),
                IntentKind.ScrollDown => SystemActions.Combo("вниз", Win32.VK_NEXT),
                IntentKind.ScrollTop => SystemActions.Combo("в начало", Win32.VK_CONTROL, Win32.VK_HOME),
                IntentKind.ScrollBottom => SystemActions.Combo("в конец", Win32.VK_CONTROL, Win32.VK_END),
                IntentKind.ZoomIn => SystemActions.Combo("крупнее", Win32.VK_CONTROL, Win32.VK_OEM_PLUS),
                IntentKind.ZoomOut => SystemActions.Combo("мельче", Win32.VK_CONTROL, Win32.VK_OEM_MINUS),
                IntentKind.ZoomReset => SystemActions.Combo("обычный масштаб", Win32.VK_CONTROL, Win32.VK_0),
                IntentKind.FullScreen => SystemActions.Combo("полный экран", Win32.VK_F11),

                // ---- Мышь и клавиши ------------------------------------------
                IntentKind.MouseClick => SystemActions.Click(),
                IntentKind.MouseRightClick => SystemActions.RightClick(),
                IntentKind.MouseDoubleClick => SystemActions.DoubleClick(),

                IntentKind.PressEnter => SystemActions.Combo("ввод", Win32.VK_RETURN),
                IntentKind.PressEscape => SystemActions.Combo("отмена", Win32.VK_ESCAPE),
                IntentKind.PressTab => SystemActions.Combo("табуляция", Win32.VK_TAB),
                IntentKind.PressBackspace => SystemActions.Combo("стёрто", Win32.VK_BACK),
                IntentKind.PressDelete => SystemActions.Combo("удалено", Win32.VK_DELETE),
                IntentKind.PressUp => SystemActions.Combo("вверх", Win32.VK_UP),
                IntentKind.PressDown => SystemActions.Combo("вниз", Win32.VK_DOWN),
                IntentKind.PressLeft => SystemActions.Combo("влево", Win32.VK_LEFT),
                IntentKind.PressRight => SystemActions.Combo("вправо", Win32.VK_RIGHT),

                // ---- Текст ----------------------------------------------------
                IntentKind.Copy => SystemActions.Combo("скопировано", Win32.VK_CONTROL, Win32.VK_C),
                IntentKind.Paste => SystemActions.Combo("вставлено", Win32.VK_CONTROL, Win32.VK_V),
                IntentKind.Cut => SystemActions.Combo("вырезано", Win32.VK_CONTROL, Win32.VK_X),
                IntentKind.Undo => SystemActions.Combo("отменено", Win32.VK_CONTROL, Win32.VK_Z),
                IntentKind.Redo => SystemActions.Combo("возвращено", Win32.VK_CONTROL, Win32.VK_Y),
                IntentKind.SelectAll => SystemActions.Combo("выделено всё", Win32.VK_CONTROL, Win32.VK_A),
                IntentKind.Save => SystemActions.Combo("сохранено", Win32.VK_CONTROL, Win32.VK_S),
                IntentKind.Print => SystemActions.Combo("печать", Win32.VK_CONTROL, Win32.VK_P),
                IntentKind.FindOnPage => SystemActions.Combo("поиск по странице", Win32.VK_CONTROL, Win32.VK_F),
                IntentKind.TypeText => SystemActions.TypeText(intent.Argument),

                // ---- Браузер --------------------------------------------------
                IntentKind.NewTab => SystemActions.Combo("новая вкладка", Win32.VK_CONTROL, Win32.VK_T),
                IntentKind.CloseTab => SystemActions.Combo("вкладка закрыта", Win32.VK_CONTROL, Win32.VK_W),
                IntentKind.ReopenTab => SystemActions.Combo("вкладка возвращена",
                    Win32.VK_CONTROL, Win32.VK_SHIFT, Win32.VK_T),
                IntentKind.NextTab => SystemActions.Combo("следующая вкладка", Win32.VK_CONTROL, Win32.VK_TAB),
                IntentKind.PreviousTab => SystemActions.Combo("предыдущая вкладка",
                    Win32.VK_CONTROL, Win32.VK_SHIFT, Win32.VK_TAB),
                IntentKind.BrowserBack => SystemActions.Combo("шаг назад", Win32.VK_MENU, Win32.VK_LEFT),
                IntentKind.BrowserForward => SystemActions.Combo("шаг вперёд", Win32.VK_MENU, Win32.VK_RIGHT),
                IntentKind.BrowserRefresh => SystemActions.Combo("страница обновлена", Win32.VK_F5),
                IntentKind.Bookmark => SystemActions.Combo("в закладках", Win32.VK_CONTROL, Win32.VK_D),
                IntentKind.IncognitoWindow => SystemActions.Combo("окно инкогнито",
                    Win32.VK_CONTROL, Win32.VK_SHIFT, Win32.VK_N),

                // ---- Места Windows --------------------------------------------
                IntentKind.OpenStartMenu => SystemActions.Combo("меню «Пуск»", Win32.VK_LWIN),
                IntentKind.OpenSearch => SystemActions.Combo("поиск Windows", Win32.VK_LWIN, Win32.VK_S),
                IntentKind.OpenSettings => SystemActions.Combo("параметры Windows", Win32.VK_LWIN, Win32.VK_I),
                IntentKind.OpenExplorer => SystemActions.Combo("проводник", Win32.VK_LWIN, Win32.VK_E),
                IntentKind.OpenTaskManager => SystemActions.Combo("диспетчер задач",
                    Win32.VK_CONTROL, Win32.VK_SHIFT, Win32.VK_ESCAPE),
                IntentKind.OpenNotifications => SystemActions.Combo("уведомления", Win32.VK_LWIN, Win32.VK_N),
                IntentKind.OpenClipboard => SystemActions.Combo("буфер обмена", Win32.VK_LWIN, Win32.VK_V),
                IntentKind.OpenEmoji => SystemActions.Combo("эмодзи", Win32.VK_LWIN, Win32.VK_OEM_PERIOD),
                IntentKind.OpenRun => SystemActions.Combo("выполнить", Win32.VK_LWIN, Win32.VK_R),

                // ---- Система ---------------------------------------------------
                IntentKind.LockWorkstation => SystemActions.LockWorkstation(),
                IntentKind.Screenshot => SystemActions.Screenshot(),
                IntentKind.Sleep => SystemActions.Sleep(),
                IntentKind.Time => SystemActions.Time(),
                IntentKind.Date => SystemActions.Date(),
                IntentKind.Battery => SystemActions.Battery(),

                IntentKind.BrightnessUp => Brightness.Step(+10),
                IntentKind.BrightnessDown => Brightness.Step(-10),
                IntentKind.BrightnessSet => Brightness.Set(Number(intent.Argument, 60)),

                IntentKind.Timer => _timers.Start(TimeSpan.FromSeconds(Number(intent.Argument, 300))),
                IntentKind.CancelTimers => _timers.CancelAll(),

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
