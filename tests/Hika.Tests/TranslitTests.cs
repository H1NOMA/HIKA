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
    // Английские гласные сочетания, звучащие по-русски одним звуком.
    [InlineData("стим", "steam")]
    [InlineData("гугл", "google")]
    [InlineData("спидтест", "speedtest")]
    [InlineData("тимс", "teams")]
    public void СовпадаетТочно(string russian, string english)
    {
        Assert.Equal(Translit.Fold(Translit.ToLatin(russian)), Translit.Fold(english));
    }

    [Theory]
    // Здесь точного совпадения нет, но расхождение в один-два символа
    // нечёткое сравнение переживает без труда.
    [InlineData("хром", "chrome")]
    [InlineData("ютуб", "youtube")]
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

    [Theory]
    // Названия игр сплошь и рядом заканчиваются цифрой, а произносят их словом.
    [InlineData("два", "2")]
    [InlineData("две", "2")]
    [InlineData("три", "3")]
    [InlineData("четыре", "4")]
    [InlineData("two", "2")]
    [InlineData("ii", "2")]
    public void ЧислительныеСовпадаютСЦифрами(string word, string digits)
    {
        Assert.Equal(1.0, FuzzyMatch.BestSimilarity(word, digits), 3);
    }

    [Fact]
    public void СловоИзДвухЧастейСЧисломНаходится()
    {
        // «халдайверс два» против «helldivers 2» — ровно то, что говорят вслух.
        var spoken = TextNormalizer.Tokenize("халдайверс два");
        var target = TextNormalizer.Tokenize("helldivers 2");

        var score = FuzzyMatch.PhraseSimilarity(spoken, target);
        Assert.True(score >= 0.62, $"совпадение всего {score:F2} — игра не найдётся");
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
