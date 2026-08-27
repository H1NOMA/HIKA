using Hika.Nlu;
using Xunit;

namespace Hika.Tests;

/// <summary>
/// Напоминания помнят, о чём они.
///
/// «Время вышло» через двадцать минут после того, как человек отвлёкся, —
/// это загадка, а не напоминание: он честно не помнит, о чём просил,
/// и вспоминать будет дольше, чем заняло бы дело.
/// </summary>
public class ReminderTests
{
    [Theory]
    [InlineData("напомни через двадцать минут выключить духовку", 1200, "выключить духовку")]
    [InlineData("напомни через десять минут позвонить маме", 600, "позвонить маме")]
    [InlineData("поставь таймер на пять минут", 300, "")]
    [InlineData("засеки полчаса", 1800, "")]
    [InlineData("напомни через час про встречу", 3600, "встречу")]
    public void ВремяИПоводРазбираютсяВместе(string said, int seconds, string note)
    {
        var intent = CommandParser.Parse(said);

        Assert.Equal(IntentKind.Timer, intent.Kind);
        Assert.Equal(seconds.ToString(), intent.Argument);
        Assert.Equal(note, intent.Note);
    }

    [Fact]
    public void ТаймерБезПоводаОстаётсяТаймером()
    {
        var intent = CommandParser.Parse("таймер на десять минут");

        Assert.Equal(IntentKind.Timer, intent.Kind);
        Assert.Equal("", intent.Note);
    }
}
