namespace Hika.Nlu;

/// <summary>
/// Как называть намерение человеку.
///
/// Существует потому, что список услышанного в окне настроек показывал
/// результат разбора так, как он называется в коде: «MediaSeekForwardFar»,
/// «OpenTaskManager», «Launch(ютуб)». Этот список — единственный ответ
/// на вопрос «почему она открыла не то», который человек может получить
/// сам, и написан он был на языке, которого он не знает. Показать
/// не-программисту английское имя перечисления значит показать ему,
/// что здесь не для него.
///
/// Названия нарочно в первом лице и в прошедшем времени: список читается
/// как рассказ о том, что произошло, а не как каталог возможностей.
/// </summary>
public static class IntentNames
{
    /// <summary>Что это была за команда, обычными словами.</summary>
    public static string Describe(Intent intent)
    {
        var name = Describe(intent.Kind);

        return string.IsNullOrWhiteSpace(intent.Argument)
            ? name
            : $"{name}: {intent.Argument}";
    }

    public static string Describe(IntentKind kind) => kind switch
    {
        IntentKind.None => "не разобрала",

        IntentKind.Launch => "запуск",
        IntentKind.Search => "поиск в интернете",

        IntentKind.FocusWindow => "переключение на окно",
        IntentKind.MinimizeWindow => "свернуть окно",
        IntentKind.MaximizeWindow => "развернуть окно",
        IntentKind.RestoreWindow => "вернуть окно",
        IntentKind.CloseWindow => "закрыть окно",
        IntentKind.ShowDesktop => "показать рабочий стол",
        IntentKind.SnapLeft => "окно влево",
        IntentKind.SnapRight => "окно вправо",
        IntentKind.SwitchWindow => "следующее окно",
        IntentKind.TaskView => "все окна",
        IntentKind.NextDesktop => "следующий рабочий стол",
        IntentKind.PreviousDesktop => "предыдущий рабочий стол",
        IntentKind.NewDesktop => "новый рабочий стол",
        IntentKind.CloseDesktop => "закрыть рабочий стол",

        IntentKind.ScrollUp => "прокрутка вверх",
        IntentKind.ScrollDown => "прокрутка вниз",
        IntentKind.ScrollTop => "в начало",
        IntentKind.ScrollBottom => "в конец",
        IntentKind.ZoomIn => "крупнее",
        IntentKind.ZoomOut => "мельче",
        IntentKind.ZoomReset => "обычный размер",
        IntentKind.FullScreen => "полный экран",

        IntentKind.MouseClick => "щелчок",
        IntentKind.MouseRightClick => "правый щелчок",
        IntentKind.MouseDoubleClick => "двойной щелчок",

        IntentKind.PressEnter => "Enter",
        IntentKind.PressEscape => "Escape",
        IntentKind.PressTab => "Tab",
        IntentKind.PressBackspace => "Backspace",
        IntentKind.PressDelete => "Delete",
        IntentKind.PressUp => "стрелка вверх",
        IntentKind.PressDown => "стрелка вниз",
        IntentKind.PressLeft => "стрелка влево",
        IntentKind.PressRight => "стрелка вправо",

        IntentKind.Copy => "копирование",
        IntentKind.Paste => "вставка",
        IntentKind.Cut => "вырезание",
        IntentKind.Undo => "отмена",
        IntentKind.Redo => "повтор",
        IntentKind.SelectAll => "выделить всё",
        IntentKind.Save => "сохранение",
        IntentKind.Print => "печать",
        IntentKind.FindOnPage => "поиск на странице",
        IntentKind.TypeText => "набор текста",

        IntentKind.VolumeUp => "громче",
        IntentKind.VolumeDown => "тише",
        IntentKind.VolumeMute => "звук выключить",
        IntentKind.VolumeSet => "громкость",

        IntentKind.MediaPlayPause => "пауза или продолжить",
        IntentKind.MediaPause => "пауза",
        IntentKind.MediaPlay => "продолжить",
        IntentKind.MediaNext => "следующий трек",
        IntentKind.MediaPrevious => "предыдущий трек",
        IntentKind.NowPlaying => "что играет",
        IntentKind.PlayMusic => "включить музыку",

        IntentKind.MediaSeekForward => "перемотка вперёд",
        IntentKind.MediaSeekBackward => "перемотка назад",
        IntentKind.MediaSeekForwardFar => "перемотка вперёд, далеко",
        IntentKind.MediaSeekBackwardFar => "перемотка назад, далеко",
        IntentKind.MediaRestart => "сначала",
        IntentKind.MediaFullScreen => "видео на весь экран",
        IntentKind.MediaTheater => "режим кинотеатра",
        IntentKind.MediaMiniPlayer => "мини-плеер",
        IntentKind.MediaCaptions => "субтитры",
        IntentKind.MediaSpeedUp => "быстрее",
        IntentKind.MediaSpeedDown => "медленнее",
        IntentKind.MediaMute => "заглушить видео",
        IntentKind.NextVideo => "следующее видео",
        IntentKind.PreviousVideo => "предыдущее видео",

        IntentKind.Help => "справка",
        IntentKind.DictationStart => "начать диктовку",
        IntentKind.DictationStop => "закончить диктовку",

        IntentKind.LockWorkstation => "заблокировать компьютер",
        IntentKind.Screenshot => "снимок экрана",
        IntentKind.Sleep => "спать",

        IntentKind.BrightnessUp => "ярче",
        IntentKind.BrightnessDown => "темнее",
        IntentKind.BrightnessSet => "яркость",

        IntentKind.Time => "который час",
        IntentKind.Date => "какое число",
        IntentKind.Battery => "заряд",
        IntentKind.Timer => "таймер",
        IntentKind.CancelTimers => "отменить таймеры",

        IntentKind.OpenStartMenu => "меню «Пуск»",
        IntentKind.OpenSearch => "поиск Windows",
        IntentKind.OpenSettings => "параметры Windows",
        IntentKind.OpenExplorer => "проводник",
        IntentKind.OpenTaskManager => "диспетчер задач",
        IntentKind.OpenNotifications => "уведомления",
        IntentKind.OpenClipboard => "буфер обмена",
        IntentKind.OpenEmoji => "эмодзи",
        IntentKind.OpenRun => "окно «Выполнить»",

        IntentKind.NewTab => "новая вкладка",
        IntentKind.CloseTab => "закрыть вкладку",
        IntentKind.ReopenTab => "вернуть вкладку",
        IntentKind.NextTab => "следующая вкладка",
        IntentKind.PreviousTab => "предыдущая вкладка",
        IntentKind.BrowserBack => "назад",
        IntentKind.BrowserForward => "вперёд",
        IntentKind.BrowserRefresh => "обновить страницу",
        IntentKind.Bookmark => "в закладки",
        IntentKind.IncognitoWindow => "окно инкогнито",

        // Новое намерение, для которого имени ещё не написали. Английское
        // имя лучше пустоты: по нему хотя бы понятно, что оно вообще есть.
        _ => kind.ToString(),
    };
}
