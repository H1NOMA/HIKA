using Hika.Catalog;
using Hika.Config;
using Hika.Nlu;
using Hika.Wake;
using Xunit;

namespace Hika.Tests;

/// <summary>
/// Разбор фразы без её исполнения.
///
/// Отвечает на вопрос, который иначе выясняется только опытом: «а что она
/// сделает, если я скажу вот так?». До сих пор единственным способом узнать
/// это было сказать вслух и посмотреть — то есть в половине случаев получить
/// открывшееся не то и потом это закрывать.
/// </summary>
public class CommandProbeTests
{
    private static HikaConfig Настройки()
    {
        var config = new HikaConfig();
        config.Wake.Words = new List<string> { "хика", "ави" };
        config.Custom = new List<CustomEntry>
        {
            new() { Phrases = new List<string> { "открой смету" }, Target = @"C:\смета.xlsx" },
        };

        return config;
    }

    private static ProbeResult Разобрать(string фраза, HikaConfig? настройки = null)
    {
        var config = настройки ?? Настройки();

        var catalog = new AppCatalog();
        catalog.Load(config);

        return CommandProbe.Explain(фраза, new WakeWordMatcher(config.Wake), catalog, config);
    }

    [Fact]
    public void ПустаяФразаНеПадает()
    {
        var result = Разобрать("   ");

        Assert.Equal(IntentKind.None, result.Intent.Kind);
        Assert.NotEmpty(result.Verdict);
    }

    [Fact]
    public void ИмяОтделяетсяОтКоманды()
    {
        var result = Разобрать("хика, сделай громче");

        Assert.Equal(IntentKind.VolumeUp, result.Intent.Kind);
        Assert.DoesNotContain("хика", result.Command, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Имя необязательно: человек проверяет команду, а не своё произношение.
    /// Но сказать, что вживую эта фраза прошла бы мимо, всё равно надо —
    /// иначе «в окошке работает, а вслух нет».
    /// </summary>
    [Fact]
    public void БезИмениРазбираетИПредупреждает()
    {
        var result = Разобрать("сделай громче");

        Assert.Equal(IntentKind.VolumeUp, result.Intent.Kind);
        Assert.Equal("", result.Name);
        Assert.Contains("Имя не прозвучало", result.Verdict);
    }

    [Fact]
    public void ОдноТолькоИмяЖдётПродолжения()
    {
        var result = Разобрать("хика");

        Assert.Contains("подождала", result.Verdict);
    }

    [Fact]
    public void СвояКомандаНаходитсяВКаталоге()
    {
        var result = Разобрать("хика, открой смету");

        Assert.Equal(IntentKind.Launch, result.Intent.Kind);
        Assert.NotEqual("", result.Target);
        Assert.Contains("открыла бы", result.Verdict);
    }

    /// <summary>
    /// Явный глагол запуска и ненайденная цель — это отказ, а не поиск.
    /// Сказавший «запусти Helldivers 2» хочет игру, а не статью о ней.
    /// </summary>
    [Fact]
    public void ЯвныйЗапускНенайденногоЗовётВСвоиКоманды()
    {
        var result = Разобрать("хика, запусти квазимодо три");

        Assert.Equal(IntentKind.Launch, result.Intent.Kind);
        Assert.Equal("", result.Target);
        Assert.Contains("Свои команды", result.Verdict);
    }

    [Fact]
    public void НеразобранноеПриВыключенномПоискеНичегоНеДелает()
    {
        var config = Настройки();
        config.Behavior.WebSearchFallback = false;

        var result = Разобрать("хика, кувырк через голову назад", config);

        Assert.Contains("ничего бы не сделала", result.Verdict);
    }

    /// <summary>
    /// Ничего не выполняет и выполнить не может: у разбора нет исполнителя
    /// команд, только каталог. Здесь это закреплено на самой опасной команде
    /// из возможных.
    /// </summary>
    [Fact]
    public void РазборНичегоНеЗапускает()
    {
        var result = Разобрать("хика, заблокируй компьютер");

        Assert.Equal(IntentKind.LockWorkstation, result.Intent.Kind);
        Assert.Contains("заблокировать компьютер", result.Verdict);
    }

    [Fact]
    public void ПриговорВсегдаНаРусском()
    {
        foreach (var фраза in new[]
                 {
                     "хика, перемотай далеко вперёд",
                     "хика, включи субтитры",
                     "хика, закрой вкладку",
                     "хика, который час",
                 })
        {
            var приговор = Разобрать(фраза).Verdict;

            Assert.True(приговор.Any(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я'),
                $"«{фраза}» -> «{приговор}»");
        }
    }
}
