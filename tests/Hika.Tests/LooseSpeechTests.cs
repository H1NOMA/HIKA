using Hika.Nlu;
using Xunit;

namespace Hika.Tests;

/// <summary>
/// Живая речь со всем, что в ней есть лишнего.
///
/// Человек не диктует команды, он разговаривает: «ну открой ко мне уже эти
/// настройки, будь добра». Команда здесь занимает два слова из восьми,
/// остальное — вежливость и привычка. Требовать говорить иначе значит
/// требовать, чтобы человек подстроился под программу.
/// </summary>
public class LooseSpeechTests
{
    [Theory]
    // Ровно те примеры, с которых всё началось.
    [InlineData("открой ко мне настройки")]
    [InlineData("запусти мне настройки")]
    [InlineData("ну открой уже настройки")]
    [InlineData("слушай открой пожалуйста настройки")]
    [InlineData("будь добра открой мне настройки")]
    [InlineData("да открой ты уже эти настройки блин")]
    [InlineData("настройки открой")]
    [InlineData("открой настройки")]
    public void ЛишниеСловаНеМешаютНайтиКоманду(string text)
    {
        Assert.Equal(IntentKind.OpenSettings, CommandParser.Parse(text).Kind);
    }

    [Theory]
    [InlineData("ну давай-ка сверни там всё", IntentKind.ShowDesktop)]
    [InlineData("слушай сделай пожалуйста потише", IntentKind.VolumeDown)]
    [InlineData("э открой мне быстренько проводник", IntentKind.OpenExplorer)]
    [InlineData("да поставь уже на паузу", IntentKind.MediaPause)]
    [InlineData("ну скопируй это пожалуйста", IntentKind.Copy)]
    [InlineData("открой мне там новую вкладку", IntentKind.NewTab)]
    [InlineData("а скажи-ка мне который час", IntentKind.Time)]
    [InlineData("ну включи мне уже мою музыку", IntentKind.PlayMusic)]
    public void ПаразитыВнеВосприятия(string text, IntentKind expected)
    {
        Assert.Equal(expected, CommandParser.Parse(text).Kind);
    }

    [Theory]
    // Порядок слов в русском свободный, и команда от перестановки не меняется.
    [InlineData("проводник открой", IntentKind.OpenExplorer)]
    [InlineData("вкладку закрой", IntentKind.CloseTab)]
    [InlineData("скриншот сделай", IntentKind.Screenshot)]
    [InlineData("музыку включи", IntentKind.PlayMusic)]
    public void ПорядокСловНеОбязателен(string text, IntentKind expected)
    {
        Assert.Equal(expected, CommandParser.Parse(text).Kind);
    }

    [Theory]
    // И вот это — цена, которую нельзя платить. Значащее слово пропускать
    // нельзя ни при каких обстоятельствах, иначе любая команда начнёт
    // совпадать с любой.
    [InlineData("включи музыку в стиме")]
    [InlineData("открой настройки стима")]
    [InlineData("открой стим")]
    [InlineData("запусти фотошоп")]
    [InlineData("включи телеграм")]
    [InlineData("открой обс студио")]
    public void ЗначащиеСловаНеПропускаются(string text)
    {
        var kind = CommandParser.Parse(text).Kind;

        Assert.True(kind is IntentKind.Launch or IntentKind.None,
            $"«{text}» разобралось как {kind} — значащее слово потерялось");
    }

    [Fact]
    public void ФразаИзОднихПаразитовКомандойНеСтановится()
    {
        // «Ну э короче слушай» — человек собирается с мыслями, а не командует.
        Assert.Equal(IntentKind.None, CommandParser.Parse("ну э короче слушай").Kind);
        Assert.Equal(IntentKind.None, CommandParser.Parse("да ну блин").Kind);
    }

    [Fact]
    public void ПечатаемоеОтПаразитовНеЧистится()
    {
        // Печатать человек может что угодно — в том числе «спасибо»
        // и «пожалуйста». Выбрасывать слова из того, что просили набрать,
        // нельзя вовсе.
        var intent = CommandParser.Parse("напечатай спасибо большое пожалуйста");

        Assert.Equal(IntentKind.TypeText, intent.Kind);
        Assert.Equal("спасибо большое пожалуйста", intent.Argument);
    }

    [Fact]
    public void ПаразитНужныйСлотуНеТеряется()
    {
        // «Это» — паразит в девяти фразах из десяти, но «закрой это»
        // держится именно на нём. Поэтому паразиты пропускаются
        // при сопоставлении, а не выбрасываются заранее: кто здесь лишний,
        // решает не список, а то, нашлось ли слову место в команде.
        Assert.Equal(IntentKind.CloseWindow, CommandParser.Parse("закрой это").Kind);
        Assert.Equal(IntentKind.Copy, CommandParser.Parse("скопируй это").Kind);
    }

    [Fact]
    public void ПредлогНужныйСлотуНеТеряется()
    {
        // «В» — не паразит: на нём держатся «перейди в начало страницы»
        // и «добавь в закладки». Оно пропускается, только если своего слота
        // для него нет.
        Assert.Equal(IntentKind.ScrollTop, CommandParser.Parse("перейди в начало страницы").Kind);
        Assert.Equal(IntentKind.Bookmark, CommandParser.Parse("добавь в закладки").Kind);
    }
}
