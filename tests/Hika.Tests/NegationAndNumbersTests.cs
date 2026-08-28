using Hika.Nlu;
using Xunit;

namespace Hika.Tests;

/// <summary>
/// Случаи, где разбор делал ровно обратное сказанному.
///
/// Все они — из одного корня: слоты сравнивают слова целиком и не знают
/// ни отрицаний, ни направления. «Выключи музыку» отличается от «включи
/// музыку» одной приставкой, «прибавь на десять» от «поставь десять» —
/// глаголом, а «семь утра» от «семь минут» — единицей, которой нет.
/// </summary>
public class NegationAndNumbersTests
{
    [Theory]
    [InlineData("выключи музыку", IntentKind.MediaPause)]
    [InlineData("отключи музыку", IntentKind.MediaPause)]
    [InlineData("выключи видео", IntentKind.MediaPause)]
    [InlineData("выруби музыку", IntentKind.MediaPause)]

    // А это должно остаться как было.
    [InlineData("включи музыку", IntentKind.PlayMusic)]
    [InlineData("включи видео", IntentKind.MediaPlay)]
    [InlineData("выключи звук", IntentKind.VolumeMute)]
    [InlineData("заглуши видео", IntentKind.MediaMute)]
    public void ОтрицаниеНеПревращаетсяВСогласие(string text, IntentKind expected)
    {
        Assert.Equal(expected, CommandParser.Parse(text).Kind);
    }

    [Theory]
    [InlineData("прибавь звук на десять", IntentKind.VolumeUp)]
    [InlineData("убавь громкость на двадцать", IntentKind.VolumeDown)]
    [InlineData("увеличь громкость на пять", IntentKind.VolumeUp)]
    [InlineData("уменьши звук на пять", IntentKind.VolumeDown)]

    // Установка числом остаётся установкой.
    [InlineData("сделай громкость тридцать", IntentKind.VolumeSet)]
    [InlineData("громкость на 50 процентов", IntentKind.VolumeSet)]
    public void ЧислоПриШагеНеСтановитсяУровнем(string text, IntentKind expected)
    {
        Assert.Equal(expected, CommandParser.Parse(text).Kind);
    }

    [Theory]
    // Будильника на время суток программа не умеет вовсе. Молча завести
    // таймер на семь минут хуже, чем не сделать ничего: человек ляжет спать,
    // а разбудит его через семь минут.
    [InlineData("поставь будильник на семь утра")]
    [InlineData("разбуди меня в восемь утра")]
    [InlineData("напомни в девять вечера")]
    public void БудильникНаВремяСутокНеСтановитсяТаймером(string text)
    {
        Assert.NotEqual(IntentKind.Timer, CommandParser.Parse(text).Kind);
    }

    [Theory]
    [InlineData("поставь таймер на пять минут", 300)]
    [InlineData("напомни через десять минут", 600)]
    [InlineData("засеки полчаса", 1800)]
    public void ОбычныйТаймерРаботаетКакРаботал(string text, int seconds)
    {
        var intent = CommandParser.Parse(text);

        Assert.Equal(IntentKind.Timer, intent.Kind);
        Assert.Equal(seconds.ToString(), intent.Argument);
    }

    [Theory]
    [InlineData("напечатай документ", IntentKind.Print)]
    [InlineData("напечатай страницу", IntentKind.Print)]
    [InlineData("распечатай документ", IntentKind.Print)]

    // А набор текста остаётся набором.
    [InlineData("напечатай привет", IntentKind.TypeText)]
    [InlineData("набери спасибо", IntentKind.TypeText)]
    public void НапечатайДокументЭтоРаспечатать(string text, IntentKind expected)
    {
        Assert.Equal(expected, CommandParser.Parse(text).Kind);
    }

    [Theory]
    [InlineData("открой вкладку", IntentKind.NewTab)]
    [InlineData("новая вкладка", IntentKind.NewTab)]
    [InlineData("верни закрытую вкладку", IntentKind.ReopenTab)]
    [InlineData("верни вкладку", IntentKind.ReopenTab)]
    public void ОткройВкладкуЭтоНоваяВкладка(string text, IntentKind expected)
    {
        Assert.Equal(expected, CommandParser.Parse(text).Kind);
    }
}
