using Hika.Nlu;
using Xunit;

namespace Hika.Tests;

/// <summary>
/// Весь набор команд разом.
///
/// Проверка нужна не ради каждой отдельной фразы, а ради того, что набор
/// стал большим. Полторы сотни формулировок неизбежно начинают спорить
/// друг с другом: «следующая вкладка» и «следующая песня» отличаются одним
/// словом, «закрой вкладку» и «закрой окно» — тоже. Такие столкновения
/// не видны при чтении кода и обнаруживаются только перебором.
/// </summary>
public class CommandSurfaceTests
{
    [Theory]
    // ---- Окна ------------------------------------------------------------
    [InlineData("закрой окно", IntentKind.CloseWindow)]
    [InlineData("сверни окно", IntentKind.MinimizeWindow)]
    [InlineData("разверни окно", IntentKind.MaximizeWindow)]
    [InlineData("сверни всё", IntentKind.ShowDesktop)]
    [InlineData("покажи рабочий стол", IntentKind.ShowDesktop)]
    [InlineData("прижми окно влево", IntentKind.SnapLeft)]
    [InlineData("подвинь окно вправо", IntentKind.SnapRight)]
    [InlineData("следующий рабочий стол", IntentKind.NextDesktop)]
    [InlineData("создай новый рабочий стол", IntentKind.NewDesktop)]

    // ---- Прокрутка и масштаб ---------------------------------------------
    [InlineData("прокрути вниз", IntentKind.ScrollDown)]
    [InlineData("листай вверх", IntentKind.ScrollUp)]
    [InlineData("перейди в начало страницы", IntentKind.ScrollTop)]
    [InlineData("перейди в конец страницы", IntentKind.ScrollBottom)]
    [InlineData("приблизь", IntentKind.ZoomIn)]
    [InlineData("уменьши масштаб", IntentKind.ZoomOut)]
    [InlineData("полный экран", IntentKind.FullScreen)]

    // ---- Текст ------------------------------------------------------------
    [InlineData("скопируй", IntentKind.Copy)]
    [InlineData("вставь", IntentKind.Paste)]
    [InlineData("вырежи это", IntentKind.Cut)]
    [InlineData("отмени действие", IntentKind.Undo)]
    [InlineData("выдели всё", IntentKind.SelectAll)]
    [InlineData("сохрани файл", IntentKind.Save)]
    [InlineData("найди на странице", IntentKind.FindOnPage)]

    // ---- Мышь и клавиши ---------------------------------------------------
    [InlineData("правый клик", IntentKind.MouseRightClick)]
    [InlineData("двойной клик", IntentKind.MouseDoubleClick)]
    [InlineData("нажми ввод", IntentKind.PressEnter)]
    [InlineData("нажми escape", IntentKind.PressEscape)]
    [InlineData("нажми стрелку вниз", IntentKind.PressDown)]

    // ---- Браузер ----------------------------------------------------------
    [InlineData("новая вкладка", IntentKind.NewTab)]
    [InlineData("закрой вкладку", IntentKind.CloseTab)]
    [InlineData("верни закрытую вкладку", IntentKind.ReopenTab)]
    [InlineData("следующая вкладка", IntentKind.NextTab)]
    [InlineData("предыдущая вкладка", IntentKind.PreviousTab)]
    [InlineData("обнови страницу", IntentKind.BrowserRefresh)]
    [InlineData("шаг вперёд", IntentKind.BrowserForward)]
    [InlineData("добавь в закладки", IntentKind.Bookmark)]
    [InlineData("открой окно инкогнито", IntentKind.IncognitoWindow)]

    // ---- Места Windows -----------------------------------------------------
    [InlineData("открой проводник", IntentKind.OpenExplorer)]
    [InlineData("открой параметры", IntentKind.OpenSettings)]
    [InlineData("открой диспетчер задач", IntentKind.OpenTaskManager)]
    [InlineData("покажи уведомления", IntentKind.OpenNotifications)]
    [InlineData("открой буфер обмена", IntentKind.OpenClipboard)]
    [InlineData("открой эмодзи", IntentKind.OpenEmoji)]
    [InlineData("открой выполнить", IntentKind.OpenRun)]

    // ---- Система ------------------------------------------------------------
    [InlineData("заблокируй компьютер", IntentKind.LockWorkstation)]
    [InlineData("сделай скриншот", IntentKind.Screenshot)]
    [InlineData("спящий режим", IntentKind.Sleep)]
    [InlineData("который час", IntentKind.Time)]
    [InlineData("какое сегодня число", IntentKind.Date)]
    [InlineData("сколько заряда осталось", IntentKind.Battery)]
    [InlineData("сделай ярче", IntentKind.BrightnessUp)]
    [InlineData("сделай темнее", IntentKind.BrightnessDown)]
    [InlineData("выключи звук", IntentKind.VolumeMute)]
    [InlineData("отмени таймер", IntentKind.CancelTimers)]
    public void КомандаУзнаётся(string text, IntentKind expected)
    {
        Assert.Equal(expected, CommandParser.Parse(text).Kind);
    }

    [Theory]
    // Пары, различающиеся одним словом. Именно здесь набор и ломается,
    // когда растёт: «следующая вкладка» против «следующей песни».
    [InlineData("следующая вкладка", IntentKind.NextTab)]
    [InlineData("следующая песня", IntentKind.MediaNext)]
    [InlineData("закрой вкладку", IntentKind.CloseTab)]
    [InlineData("закрой окно", IntentKind.CloseWindow)]
    [InlineData("предыдущая вкладка", IntentKind.PreviousTab)]
    [InlineData("предыдущий трек", IntentKind.MediaPrevious)]
    public void СоседниеКомандыНеПутаются(string text, IntentKind expected)
    {
        Assert.Equal(expected, CommandParser.Parse(text).Kind);
    }

    [Theory]
    // Ни одна из полутора сотен формулировок не должна перехватывать запуск
    // программы. Это главный риск большого набора и главная причина,
    // по которой пороги стоят высоко.
    [InlineData("открой стим")]
    [InlineData("запусти фотошоп")]
    [InlineData("включи телеграм")]
    [InlineData("открой ютуб")]
    [InlineData("запусти халдайверс два")]
    [InlineData("открой обс студио")]
    [InlineData("запусти вижуал студио код")]
    [InlineData("открой дискорд")]
    public void ЗапускПрограммыНеПерехватывается(string text)
    {
        Assert.Equal(IntentKind.Launch, CommandParser.Parse(text).Kind);
    }

    [Theory]
    [InlineData("напечатай привет мир", "привет мир")]
    [InlineData("набери спасибо", "спасибо")]
    [InlineData("введи логин", "логин")]
    public void НаборТекстаОтделёнОтСочинения(string text, string expected)
    {
        var intent = CommandParser.Parse(text);

        Assert.Equal(IntentKind.TypeText, intent.Kind);
        Assert.Equal(expected, intent.Argument);
    }

    [Fact]
    public void НапишиЭтоПросьбаСочинитьАНеНапечатать()
    {
        // Иначе на «напиши письмо другу» в активное окно вобьётся
        // текст «письмо другу».
        Assert.NotEqual(IntentKind.TypeText, CommandParser.Parse("напиши письмо другу").Kind);
    }

    [Fact]
    public void ВесьНаборРазбираетсяБезСтолкновений()
    {
        // Каждая команда должна узнаваться хотя бы одной своей формулировкой.
        // Проверка ловит опечатки в шаблонах: слот, в котором ошиблись буквой,
        // молча перестаёт совпадать с чем бы то ни было.
        var covered = CommandCatalog.All.Select(p => p.Kind).Distinct().ToHashSet();

        Assert.True(covered.Count >= 40, $"шаблонов подозрительно мало: {covered.Count}");
        Assert.DoesNotContain(IntentKind.None, covered);
    }
}
