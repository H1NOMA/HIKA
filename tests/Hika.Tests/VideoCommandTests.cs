using Hika.Nlu;
using Xunit;

namespace Hika.Tests;

/// <summary>
/// Управление видео — ютубом и любым плеером.
///
/// Раздел выделен отдельно потому, что он опаснее прочих. Список сеансов
/// Windows знает три команды — играй, стой, дальше, — а всё остальное
/// делается клавишами, которые достаются окну впереди. Ошибка здесь
/// не «не сработало», а «в переписку уехала буква f», и цена у неё выше,
/// чем у любой другой неузнанной команды.
///
/// Поэтому проверяется не только то, что команды узнаются, но и то,
/// что они не перехватывают соседей: перемотка — шаг назад в браузере,
/// звук видео — общесистемную тишину, полный экран — F11.
/// </summary>
public class VideoCommandTests
{
    [Theory]
    // ---- Перемотка --------------------------------------------------------
    [InlineData("перемотай", IntentKind.MediaSeekForward)]
    [InlineData("перемотай вперёд", IntentKind.MediaSeekForward)]
    [InlineData("промотай вперёд", IntentKind.MediaSeekForward)]
    [InlineData("перемотай немного вперёд", IntentKind.MediaSeekForward)]
    [InlineData("перемотай видео вперёд", IntentKind.MediaSeekForward)]
    [InlineData("перемотай назад", IntentKind.MediaSeekBackward)]
    [InlineData("отмотай назад", IntentKind.MediaSeekBackward)]
    [InlineData("промотай немного назад", IntentKind.MediaSeekBackward)]
    [InlineData("перемотай на минуту", IntentKind.MediaSeekForwardFar)]
    [InlineData("перемотай подальше", IntentKind.MediaSeekForwardFar)]
    [InlineData("перемотай видео на минуту вперёд", IntentKind.MediaSeekForwardFar)]
    [InlineData("перемотай назад на минуту", IntentKind.MediaSeekBackwardFar)]
    [InlineData("отмотай подальше", IntentKind.MediaSeekBackwardFar)]
    [InlineData("отмотай подальше назад", IntentKind.MediaSeekBackwardFar)]

    // «Отмотай» само по себе значит назад — приставка уже сказала куда.
    [InlineData("отмотай", IntentKind.MediaSeekBackward)]
    [InlineData("промотай", IntentKind.MediaSeekForward)]
    [InlineData("отмотай вперёд", IntentKind.MediaSeekForward)]

    // Число внутри фразы шаблону не даётся — его разбирает отдельная ветка.
    [InlineData("перемотай на десять секунд", IntentKind.MediaSeekForward)]
    [InlineData("отмотай на пять секунд", IntentKind.MediaSeekBackward)]
    [InlineData("перемотай на тридцать секунд назад", IntentKind.MediaSeekBackwardFar)]
    [InlineData("перемотай на две минуты вперёд", IntentKind.MediaSeekForwardFar)]

    // ---- Начало и переключение --------------------------------------------
    [InlineData("включи сначала", IntentKind.MediaRestart)]
    [InlineData("начни заново", IntentKind.MediaRestart)]
    [InlineData("перемотай в начала", IntentKind.MediaRestart)]
    [InlineData("следующее видео", IntentKind.NextVideo)]
    [InlineData("включи следующий ролик", IntentKind.NextVideo)]
    [InlineData("предыдущее видео", IntentKind.PreviousVideo)]

    // ---- Экран -------------------------------------------------------------
    [InlineData("разверни видео", IntentKind.MediaFullScreen)]
    [InlineData("видео на весь экран", IntentKind.MediaFullScreen)]
    [InlineData("раскрой ролик", IntentKind.MediaFullScreen)]
    [InlineData("полный экран видео", IntentKind.MediaFullScreen)]
    [InlineData("выйди из полного экрана", IntentKind.MediaFullScreen)]
    [InlineData("выключи полный экран", IntentKind.MediaFullScreen)]
    [InlineData("включи театральный режим", IntentKind.MediaTheater)]
    [InlineData("широкий режим", IntentKind.MediaTheater)]
    [InlineData("включи мини плеер", IntentKind.MediaMiniPlayer)]

    // ---- Субтитры и скорость ------------------------------------------------
    [InlineData("включи субтитры", IntentKind.MediaCaptions)]
    [InlineData("выключи субтитры", IntentKind.MediaCaptions)]
    [InlineData("покажи титры", IntentKind.MediaCaptions)]
    [InlineData("сделай быстрее", IntentKind.MediaSpeedUp)]
    [InlineData("ускорь видео", IntentKind.MediaSpeedUp)]
    [InlineData("помедленнее", IntentKind.MediaSpeedDown)]
    [InlineData("замедли видео", IntentKind.MediaSpeedDown)]

    // ---- Звук плеера ---------------------------------------------------------
    [InlineData("заглуши видео", IntentKind.MediaMute)]
    [InlineData("выключи звук видео", IntentKind.MediaMute)]
    [InlineData("верни звук видео", IntentKind.MediaMute)]

    // ---- Пауза и продолжение через сеансы -------------------------------------
    [InlineData("поставь видео на паузу", IntentKind.MediaPause)]
    [InlineData("останови видео", IntentKind.MediaPause)]
    [InlineData("включи видео", IntentKind.MediaPlay)]
    [InlineData("продолжи видео", IntentKind.MediaPlay)]
    public void КомандаВидеоУзнаётся(string text, IntentKind expected)
    {
        Assert.Equal(expected, CommandParser.Parse(text).Kind);
    }

    [Theory]
    // Соседи, которых видео не должно перехватывать. Каждая пара отличается
    // одним словом, и без проверки такие столкновения обнаруживаются только
    // в живой речи — то есть поздно.
    [InlineData("шаг назад", IntentKind.BrowserBack)]
    [InlineData("вернись назад", IntentKind.BrowserBack)]
    [InlineData("отмени действие", IntentKind.Undo)]
    [InlineData("выключи звук", IntentKind.VolumeMute)]
    [InlineData("полный экран", IntentKind.FullScreen)]
    [InlineData("разверни окно", IntentKind.MaximizeWindow)]
    [InlineData("сверни окно", IntentKind.MinimizeWindow)]
    [InlineData("следующий трек", IntentKind.MediaNext)]
    [InlineData("следующая песня", IntentKind.MediaNext)]
    [InlineData("следующая вкладка", IntentKind.NextTab)]
    [InlineData("включи музыку", IntentKind.PlayMusic)]
    [InlineData("перейди в начало страницы", IntentKind.ScrollTop)]
    [InlineData("сделай тише", IntentKind.VolumeDown)]
    [InlineData("сделай громче", IntentKind.VolumeUp)]
    [InlineData("поставь таймер на десять секунд", IntentKind.Timer)]
    [InlineData("громкость тридцать", IntentKind.VolumeSet)]
    public void СоседниеКомандыВидеоНеПерехватывает(string text, IntentKind expected)
    {
        Assert.Equal(expected, CommandParser.Parse(text).Kind);
    }

    [Theory]
    // И главное: запуск программ должен остаться запуском. Ютуб открывают
    // чаще, чем перематывают, и «открой ютуб» обязано открывать ютуб.
    [InlineData("открой ютуб")]
    [InlineData("включи ютуб")]
    [InlineData("запусти видеоредактор")]
    [InlineData("открой видеокарту")]
    public void ЗапускНеПерехватывается(string text)
    {
        var kind = CommandParser.Parse(text).Kind;

        Assert.True(kind is IntentKind.Launch,
            $"«{text}» разобралось как {kind}, а должно было остаться запуском");
    }

    [Theory]
    // Живая речь со всем лишним: команда видео должна узнаваться так же,
    // как любая другая.
    [InlineData("ну перемотай там немного вперёд", IntentKind.MediaSeekForward)]
    [InlineData("слушай включи пожалуйста субтитры", IntentKind.MediaCaptions)]
    [InlineData("да разверни ты уже это видео", IntentKind.MediaFullScreen)]
    [InlineData("давай-ка следующее видео", IntentKind.NextVideo)]
    [InlineData("видео разверни", IntentKind.MediaFullScreen)]
    public void ПаразитыИПорядокНеМешают(string text, IntentKind expected)
    {
        Assert.Equal(expected, CommandParser.Parse(text).Kind);
    }
}
