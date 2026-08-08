using Hika.Config;
using Hika.Wake;
using Xunit;

namespace Hika.Tests;

public class PersonaTests
{
    [Fact]
    public void ЛичностьНаходитсяПоИдентификатору()
    {
        Assert.Equal("Хика", Personas.ById("hika").Name);
        Assert.Equal("Ави", Personas.ById("avi").Name);
    }

    [Fact]
    public void НеизвестнаяЛичностьОткатываетсяКПервой()
    {
        // Испорченный config.json не повод остаться без имени вовсе.
        Assert.Equal(Personas.Hika.Id, Personas.ById("что-то не то").Id);
        Assert.Equal(Personas.Hika.Id, Personas.ById(null).Id);
    }

    [Fact]
    public void ГлавноеИмяИдётПервым()
    {
        // По первому имени подписывается интерфейс, поэтому порядок значим.
        Assert.Equal("хика", Personas.WakeWordsFor("hika", respondToBoth: false)[0]);
        Assert.Equal("ави", Personas.WakeWordsFor("avi", respondToBoth: false)[0]);
    }

    [Fact]
    public void БезВторойЛичностиЕёИмёнВСпискеНет()
    {
        var words = Personas.WakeWordsFor("avi", respondToBoth: false);

        Assert.Contains("ави", words);
        Assert.DoesNotContain("хика", words);
        Assert.DoesNotContain("хико", words);
    }

    [Fact]
    public void СВключённымОбоимиРаботаютВсеИмена()
    {
        var words = Personas.WakeWordsFor("avi", respondToBoth: true);

        Assert.Contains("ави", words);
        Assert.Contains("хика", words);
        Assert.Contains("хико", words);
    }

    [Fact]
    public void УЛичностейРазныеЦвета()
    {
        // Цвет — единственное, по чему видно выбранную личность боковым зрением.
        Assert.NotEqual(Personas.Hika.Accent, Personas.Avi.Accent);
        Assert.Equal(4, Personas.Hika.GlowColors.Count);
        Assert.Equal(4, Personas.Avi.GlowColors.Count);
    }

    [Theory]
    [InlineData("hika", "Хико, открой ютуб")]
    [InlineData("avi", "Ави, открой ютуб")]
    public void ВыбраннаяЛичностьОтзываетсяНаСвоёИмя(string persona, string spoken)
    {
        var config = new WakeConfig { Words = Personas.WakeWordsFor(persona, respondToBoth: false) };
        Assert.True(new WakeWordMatcher(config).Match(spoken).Matched);
    }

    [Fact]
    public void БезВторойЛичностиЧужоеИмяНеСрабатывает()
    {
        var config = new WakeConfig { Words = Personas.WakeWordsFor("avi", respondToBoth: false) };
        Assert.False(new WakeWordMatcher(config).Match("Хико, открой ютуб").Matched);
    }
}
