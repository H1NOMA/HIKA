using Hika.Nlu;
using Hika.Speech;
using Xunit;

namespace Hika.Tests;

/// <summary>
/// Диктовка.
///
/// Проверяется прежде всего выход из неё. Диктовка, из которой нельзя
/// выбраться, — это программа, набирающая ваш разговор с домашними в чужую
/// переписку, и цена такой ошибки выше любой пользы от самой возможности.
/// </summary>
public class DictationTests
{
    [Theory]
    [InlineData("начни диктовку")]
    [InlineData("включи диктовку")]
    [InlineData("режим диктовки")]
    [InlineData("диктую")]
    [InlineData("записывай за мной")]
    [InlineData("печатай за мной")]
    [InlineData("давай печатай всё")]
    [InlineData("напечатай за мной")]
    [InlineData("пиши за мной")]
    [InlineData("набирай под диктовку")]
    public void ДиктовкаНачинаетсяРазнымиСловами(string text)
    {
        Assert.Equal(IntentKind.DictationStart, CommandParser.Parse(text).Kind);
    }

    [Theory]
    [InlineData("закончи диктовку")]
    [InlineData("хватит печатать")]
    [InlineData("стоп диктовка")]
    [InlineData("прекрати диктовку")]
    [InlineData("останови диктовку")]
    public void ДиктовкаЗаканчиваетсяРазнымиСловами(string text)
    {
        Assert.Equal(IntentKind.DictationStop, CommandParser.Parse(text).Kind);
    }

    [Theory]
    // Ровно тот же список — но глазами самой диктовки. Пока эти два списка
    // жили порознь, они разъехались: окно настроек обещало «закончи диктовку»,
    // тест это закреплял, а диктовка печатала фразу в текст.
    [InlineData("закончи диктовку")]
    [InlineData("хватит печатать")]
    [InlineData("стоп диктовка")]
    [InlineData("прекрати диктовку")]
    [InlineData("останови диктовку")]
    public void ТемиЖеСловамиДиктовкаИЗаканчивается(string text)
    {
        Assert.True(Dictation.IsStop(text), $"«{text}» напечаталось бы вместо остановки");
    }

    [Theory]
    [InlineData("стоп")]
    [InlineData("хватит")]
    [InlineData("всё")]
    [InlineData("достаточно")]
    [InlineData("конец диктовки")]
    [InlineData("хватит печатать")]
    [InlineData("всё хватит")]
    public void КороткоеСловоЗаканчиваетДиктовку(string text)
    {
        Assert.True(Dictation.IsStop(text), $"«{text}» должно было закончить диктовку");
    }

    [Theory]
    // И вот это — то, ради чего проверка на длину вообще существует.
    // Продиктованное «я сказал ему хватит» не должно обрывать диктовку
    // на полуслове: после такого возможностью перестают пользоваться.
    [InlineData("я сказал ему хватит")]
    [InlineData("стоп машина это была шутка")]
    [InlineData("всё это уже давно закончилось")]
    [InlineData("привет как дела")]
    [InlineData("достаточно интересная мысль")]
    public void ДлиннаяФразаНеОбрываетДиктовку(string text)
    {
        Assert.False(Dictation.IsStop(text), $"«{text}» оборвало диктовку, а это продиктованный текст");
    }

    [Fact]
    public void ПустаяФразаНеОбрываетДиктовку()
    {
        Assert.False(Dictation.IsStop(""));
        Assert.False(Dictation.IsStop("   "));
    }

    // ---- Знаки препинания ---------------------------------------------------

    [Theory]
    [InlineData("привет запятая как дела", "Привет, как дела")]
    [InlineData("это конец точка", "Это конец.")]
    [InlineData("ты идёшь вопросительный знак", "Ты идёшь?")]
    [InlineData("ура восклицательный знак", "Ура!")]
    [InlineData("вот что двоеточие список", "Вот что: список")]
    public void НазванныеЗнакиСтановятсяЗнаками(string said, string expected)
    {
        Assert.Equal(expected, Dictation.Punctuate(said));
    }

    [Fact]
    public void ЗнакПриклеиваетсяКСловуБезПробела()
    {
        Assert.Equal("Привет, друг.", Dictation.Punctuate("привет запятая друг точка"));
    }

    [Fact]
    public void СвояТочкаВытесняетЧужую()
    {
        // Whisper уже поставил точку в конце фразы, а человек назвал знак
        // вслух. Два знака подряд — верный признак, что один лишний.
        Assert.Equal("Привет,", Dictation.Punctuate("Привет. запятая"));
        Assert.Equal("Готово!", Dictation.Punctuate("Готово. восклицательный знак"));
    }

    [Fact]
    public void ПослеТочкиСледующееСловоСБольшой()
    {
        Assert.Equal("Раз. Два. Три",
            Dictation.Punctuate("раз точка два точка три"));
    }

    [Fact]
    public void НоваяСтрокаЭтоПереносАНеСлова()
    {
        var text = Dictation.Punctuate("первая строка новая строка вторая");

        Assert.Contains("\n", text);
        Assert.DoesNotContain("новая строка", text);
        Assert.EndsWith("Вторая", text);
    }

    [Fact]
    public void ОбычныйТекстНеПортится()
    {
        // Ничего похожего на знаки — значит, ничего и не должно меняться,
        // кроме заглавной в начале.
        Assert.Equal("Купить хлеба и молока по дороге домой",
            Dictation.Punctuate("купить хлеба и молока по дороге домой"));
    }

    [Fact]
    public void УжеЗаглавноеОстаётсяЗаглавным()
    {
        Assert.Equal("Москва большая", Dictation.Punctuate("Москва большая"));
    }

    [Fact]
    public void ПустоеОстаётсяПустым()
    {
        Assert.Equal("", Dictation.Punctuate(""));
        Assert.Equal("", Dictation.Punctuate("   "));
    }

    [Fact]
    public void ПродолжениеПредложенияНеНачинаетсяСЗаглавной()
    {
        // Диктовка идёт кусками: человек говорит «я пошёл в магазин», молчит,
        // говорит «и купил хлеба». Распознавание видит два отдельных куска
        // и каждый начинает с заглавной — а это одно предложение.
        Assert.Equal("и купил хлеба", Dictation.Punctuate("И купил хлеба", startsSentence: false));
        Assert.Equal("И купил хлеба", Dictation.Punctuate("и купил хлеба", startsSentence: true));
    }

    [Fact]
    public void АббревиатураОстаётсяАббревиатурой()
    {
        Assert.Equal("МЧС приехало", Dictation.Punctuate("МЧС приехало", startsSentence: false));
    }

    [Theory]
    [InlineData("Это конец.", true)]
    [InlineData("Вопрос?", true)]
    [InlineData("а дальше", false)]
    [InlineData("Привет,", false)]
    [InlineData("", true)]
    public void КонецПредложенияВиденПоПоследнемуЗнаку(string typed, bool ends)
    {
        Assert.Equal(ends, Dictation.EndsSentence(typed));
    }

    [Theory]
    // Останавливать диктовку человек будет так, как привык обращаться:
    // «Хика, стоп». Имя снимается разбором обращения, а сюда приходит остаток —
    // и он обязан узнаваться.
    [InlineData("стоп")]
    [InlineData("хватит")]
    [InlineData("хватит диктовать")]
    public void ОстатокПослеИмениУзнаётсяКакСтоп(string rest)
    {
        Assert.True(Dictation.IsStop(rest));
    }

    [Theory]
    // Команды не должны становиться диктовкой, а диктовка — командами.
    [InlineData("открой стим", IntentKind.Launch)]
    [InlineData("напечатай привет", IntentKind.TypeText)]
    [InlineData("следующий трек", IntentKind.MediaNext)]
    public void СоседниеКомандыНеПутаютсяСДиктовкой(string text, IntentKind expected)
    {
        Assert.Equal(expected, CommandParser.Parse(text).Kind);
    }
}
