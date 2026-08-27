using Hika.Diagnostics;
using Xunit;

namespace Hika.Tests;

/// <summary>
/// Вывод по последним фразам.
///
/// Три разные беды выглядят снаружи одинаково — «не работает», — а лечатся
/// в трёх разных местах: имя не узнаётся, команда не разбирается, программа
/// не находится. Сказать, какая из трёх, может только тот, кто видит все
/// фразы разом. Если он скажет неверно, человек полдня будет крутить не то.
/// </summary>
public class RecentCommandsTests
{
    private static Heard Item(HeardOutcome outcome, string text = "открой стим")
        => new(text, "команда", "", outcome, 900, 0.9);

    [Fact]
    public void ПоДвумФразамВыводовНеДелают()
    {
        var recent = new RecentCommands();
        recent.Add(Item(HeardOutcome.NotForUs));
        recent.Add(Item(HeardOutcome.NotForUs));

        Assert.Equal("", recent.Verdict());
    }

    [Fact]
    public void ЕслиНичегоНеПринятоЗаОбращениеДелоВИмени()
    {
        var recent = new RecentCommands();
        for (int i = 0; i < 5; i++) recent.Add(Item(HeardOutcome.NotForUs));

        Assert.Contains("произношени", recent.Verdict());
    }

    [Fact]
    public void ЕслиИмяУзнаётсяАКомандыНетДелоВРаспознавании()
    {
        var recent = new RecentCommands();
        for (int i = 0; i < 4; i++) recent.Add(Item(HeardOutcome.NotUnderstood));

        var verdict = recent.Verdict();

        Assert.Contains("коверкает", verdict);
        Assert.DoesNotContain("произношени", verdict);
    }

    [Fact]
    public void ЕслиКомандыРазбираютсяНоНеВыполняютсяДелоВПоиске()
    {
        var recent = new RecentCommands();
        for (int i = 0; i < 4; i++) recent.Add(Item(HeardOutcome.Failed));

        Assert.Contains("Уверенность", recent.Verdict());
    }

    [Fact]
    public void КогдаВсёРаботаетСоветовНет()
    {
        var recent = new RecentCommands();
        for (int i = 0; i < 5; i++) recent.Add(Item(HeardOutcome.Done));

        Assert.Equal("", recent.Verdict());
    }

    [Fact]
    public void ПоследниеСверхуИСтарыеЗабываются()
    {
        var recent = new RecentCommands();
        for (int i = 0; i < 40; i++) recent.Add(Item(HeardOutcome.Done, $"фраза {i}"));

        var items = recent.Items();

        Assert.True(items.Count <= 12, $"накопилось {items.Count}");
        Assert.Equal("фраза 39", items[0].Text);
    }
}
