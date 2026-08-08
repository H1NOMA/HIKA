using Hika.Config;
using Hika.Wake;
using Xunit;

namespace Hika.Tests;

/// <summary>
/// Слово пробуждения — место, где ошибка стоит дороже всего: не услышит
/// имя, и ассистента как будто нет вовсе; услышит лишнего, и он полезет
/// открывать программы посреди разговора.
/// </summary>
public class WakeWordTests
{
    private static WakeWordMatcher Matcher() => new(new WakeConfig());

    [Theory]
    [InlineData("Ави, открой ютуб", "ави", "открой ютуб")]
    [InlineData("Хика, запусти ворд", "хика", "запусти ворд")]
    [InlineData("Привет, Ави, открой гугл", "ави", "открой гугл")]
    [InlineData("Эй, Хика, включи музыку", "хика", "включи музыку")]
    [InlineData("Hey Avi, open Word", "ави", "open word")]
    [InlineData("Хико, открой ютуб", "хико", "открой ютуб")]
    [InlineData("Привет, Хико, запусти телеграм", "хико", "запусти телеграм")]
    [InlineData("Ави ютуб", "ави", "ютуб")]
    [InlineData("Окей, Ави, сделай громче", "ави", "сделай громче")]
    public void УзнаётИмяИОтделяетКоманду(string spoken, string expectedWord, string expectedRest)
    {
        var match = Matcher().Match(spoken);

        Assert.True(match.Matched, $"имя не найдено в «{spoken}»");
        Assert.Equal(expectedWord, match.Word);
        Assert.Equal(expectedRest, match.Rest);
    }

    [Theory]
    // Так распознаватель речи коверкает имена на практике.
    [InlineData("Авви открой ютуб")]
    [InlineData("Авиа открой ютуб")]
    [InlineData("Авия открой ютуб")]
    [InlineData("Хико запусти ворд")]
    [InlineData("Кика запусти ворд")]
    [InlineData("Хикко открой гугл")]
    [InlineData("Чико открой гугл")]
    [InlineData("Hiko open google")]
    [InlineData("Avi open youtube")]
    public void ПрощаетИскажения(string spoken)
    {
        Assert.True(Matcher().Match(spoken).Matched, $"не узнало имя в «{spoken}»");
    }

    [Theory]
    // Порог узнавания имени щедрый, и слова на «хи» страдают от этого первыми.
    [InlineData("хиты этого года")]
    [InlineData("хитро придумано")]
    [InlineData("тихо всё было")]
    public void СловаНаХиНеБудятАссистента(string spoken)
    {
        var match = Matcher().Match(spoken);
        Assert.False(match.Matched, $"ложно сработало на «{spoken}» (услышало «{match.Word}»)");
    }

    [Theory]
    // Обычная речь не должна будить ассистента.
    [InlineData("мне надо позвонить маме")]
    [InlineData("они пришли вчера вечером")]
    [InlineData("иди сюда пожалуйста")]
    [InlineData("открой ютуб")]
    [InlineData("это было довольно странно")]
    [InlineData("как дела")]
    public void НеСрабатываетНаОбычнуюРечь(string spoken)
    {
        var match = Matcher().Match(spoken);
        Assert.False(match.Matched, $"ложно сработало на «{spoken}» (услышало «{match.Word}»)");
    }

    [Fact]
    public void ИмяБезКомандыПереводитВОжидание()
    {
        var match = Matcher().Match("Ави");

        Assert.True(match.Matched);
        Assert.True(match.IsBareCall);
    }

    [Fact]
    public void СклеиваетРазорванноеИмя()
    {
        // Распознаватель нередко разбивает короткое имя на два слова.
        var match = Matcher().Match("а ви открой ютуб");

        Assert.True(match.Matched);
        Assert.Equal("открой ютуб", match.Rest);
    }

    [Fact]
    public void РазделяетСлипшеесяИмя()
    {
        var match = Matcher().Match("авиоткрой ютуб");

        Assert.True(match.Matched);
        Assert.Contains("открой", match.Rest);
    }

    [Fact]
    public void ДописанныеВариантыРаботают()
    {
        var config = new WakeConfig();
        config.ExtraVariants.Add("обои");

        var match = new WakeWordMatcher(config).Match("обои открой ютуб");

        Assert.True(match.Matched);
        Assert.Equal("открой ютуб", match.Rest);
    }

    [Fact]
    public void БезДописыванияОпасныйДвойникНеПроходит()
    {
        // «Обои» — настоящее слово, и по умолчанию оно не должно будить ассистента.
        Assert.False(Matcher().Match("обои открой ютуб").Matched);
    }

    [Fact]
    public void ПоУмолчаниюИмяИщетсяТолькоВНачале()
    {
        Assert.False(Matcher().Match("я вчера видел ави в городе").Matched);
    }

    [Fact]
    public void СВключённымПоискомВездеИмяНаходитсяВСередине()
    {
        var config = new WakeConfig { AllowAnywhere = true };
        Assert.True(new WakeWordMatcher(config).Match("слушай а ну ави открой ютуб").Matched);
    }
}
