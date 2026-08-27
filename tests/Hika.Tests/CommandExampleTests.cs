using Hika.Nlu;
using Xunit;

namespace Hika.Tests;

/// <summary>
/// Список команд, который видит человек, обязан быть правдой.
///
/// Подсказка, которая врёт, хуже отсутствующей: человек говорит написанное,
/// ничего не происходит, и вывод он делает не про эту фразу, а про всю
/// программу. Поэтому каждый пример из списка проверяется на то, что он
/// действительно разбирается в обещанную команду.
/// </summary>
public class CommandExampleTests
{
    public static TheoryData<string, IntentKind> Examples()
    {
        var data = new TheoryData<string, IntentKind>();
        foreach (var example in CommandExamples.Flat()) data.Add(example.Say, example.Kind);
        return data;
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void ПримерДелаетТоЧтоОбещает(string say, IntentKind expected)
    {
        Assert.Equal(expected, CommandParser.Parse(say).Kind);
    }

    [Fact]
    public void СписокНеПустИНеВырожден()
    {
        var groups = CommandExamples.All;

        Assert.True(groups.Length >= 8, $"разделов подозрительно мало: {groups.Length}");
        Assert.All(groups, g => Assert.NotEmpty(g.Examples));
        Assert.All(groups, g => Assert.False(string.IsNullOrWhiteSpace(g.Title)));
    }

    [Fact]
    public void ПримерыНеПовторяются()
    {
        var said = CommandExamples.Flat().Select(e => e.Say).ToList();

        Assert.Equal(said.Count, said.Distinct().Count());
    }
}
