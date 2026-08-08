using Hika.Nlu;
using Xunit;

namespace Hika.Tests;

/// <summary>
/// Сведение кириллицы и латиницы к общему звучанию — несущая конструкция
/// всего распознавания команд. Если она сломается, «фотошоп» перестанет
/// находить Photoshop, и заметить это без тестов будет негде.
/// </summary>
public class TranslitTests
{
    [Theory]
    // Пары, ради которых всё и затевалось: человек говорит по-русски,
    // программа называется по-английски.
    [InlineData("фотошоп", "photoshop")]
    [InlineData("ворд", "word")]
    [InlineData("эксель", "excel")]
    [InlineData("твич", "twitch")]
    [InlineData("телеграм", "telegram")]
    [InlineData("дискорд", "discord")]
    [InlineData("эксплорер", "explorer")]
    public void СовпадаетТочно(string russian, string english)
    {
        Assert.Equal(Translit.Fold(Translit.ToLatin(russian)), Translit.Fold(english));
    }

    [Theory]
    // Здесь точного совпадения нет, но расхождение в один-два символа
    // нечёткое сравнение переживает без труда.
    [InlineData("хром", "chrome")]
    [InlineData("ютуб", "youtube")]
    [InlineData("стим", "steam")]
    [InlineData("гугл", "google")]
    [InlineData("спотифай", "spotify")]
    [InlineData("гитхаб", "github")]
    public void СовпадаетДостаточноБлизко(string russian, string english)
    {
        var score = FuzzyMatch.BestSimilarity(russian, english);
        Assert.True(score >= 0.7, $"«{russian}» против «{english}» дало всего {score:F2}");
    }

    [Theory]
    // Разные вещи не должны сливаться: иначе «ворд» начнёт открывать что попало.
    [InlineData("ворд", "excel")]
    [InlineData("хром", "telegram")]
    [InlineData("ютуб", "photoshop")]
    [InlineData("стим", "google")]
    public void РазныеСловаНеСливаются(string a, string b)
    {
        var score = FuzzyMatch.BestSimilarity(a, b);
        Assert.True(score < 0.6, $"«{a}» и «{b}» оказались похожи на {score:F2} — это слишком много");
    }

    [Fact]
    public void СвёрткаУбираетУдвоенияИНемуюE()
    {
        Assert.Equal("telegram", Translit.Fold("telegramm"));
        Assert.Equal("chrom", Translit.Fold("chrome"));
    }

    [Fact]
    public void ЁСводитсяКЕ()
    {
        Assert.Equal(TextNormalizer.Normalize("ещё"), TextNormalizer.Normalize("еще"));
    }

    [Fact]
    public void ЗнакиПрепинанияИРегистрНеМешают()
    {
        Assert.Equal("ави открой ютуб", TextNormalizer.Normalize("Ави, открой ЮТУБ!"));
    }

    [Fact]
    public void ЛатинскиеДвойникиВнутриКириллицыЧинятся()
    {
        // «xром» с латинской «x» — Whisper временами смешивает алфавиты.
        Assert.Equal("хром", TextNormalizer.FixMixedAlphabet("xром"));
    }
}
