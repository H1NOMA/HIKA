using System.Text;
using Hika.Nlu;
using Hika.Speech;
using Xunit;

namespace Hika.Tests;

/// <summary>
/// Граница между командой и вопросом. Ошибка в любую сторону обходится дорого:
/// команда, ушедшая в разговор, — это секунда ожидания и вежливый ответ вместо
/// запущенной программы; вопрос, ушедший в каталог, — запущенная программа
/// вместо ответа.
/// </summary>
public class ConversationTests
{
    [Theory]
    [InlineData("расскажи анекдот")]
    [InlineData("объясни как работает вулкан")]
    [InlineData("что такое квантовая запутанность")]
    [InlineData("почему небо голубое")]
    [InlineData("посоветуй фильм на вечер")]
    [InlineData("переведи слово серендипность")]
    [InlineData("как дела")]
    [InlineData("давай поговорим")]
    public void ЯвныйВопросУходитВРазговорСразу(string text)
    {
        Assert.True(Conversation.IsDefinitelyTalk(text), $"«{text}» должно уйти в разговор");
    }

    [Theory]
    // Всё это надо исполнить, а не обсудить.
    [InlineData("открой ютуб")]
    [InlineData("запусти халдайверс два")]
    [InlineData("включи музыку")]
    [InlineData("сверни все")]
    [InlineData("сделай скриншот")]
    [InlineData("покажи рабочий стол")]
    [InlineData("громче")]
    public void КомандаВРазговорНеУходит(string text)
    {
        Assert.False(Conversation.IsDefinitelyTalk(text), $"«{text}» — это команда");
        Assert.False(Conversation.MightBeTalk(text), $"«{text}» — это команда даже при слабой проверке");
    }

    [Fact]
    public void ГлаголЗапускаПеребиваетВопросительноеНачало()
    {
        // «Расскажи» здесь есть, но и «открой» тоже — и открыть важнее.
        Assert.False(Conversation.IsDefinitelyTalk("расскажи и открой ютуб"));
    }

    [Theory]
    [InlineData("сколько лететь до марса")]
    [InlineData("где находится байкал")]
    [InlineData("это вообще нормально или нет?")]
    public void СлабыйПризнакСрабатываетТолькоВЗапаснойПроверке(string text)
    {
        Assert.True(Conversation.MightBeTalk(text), $"«{text}» стоит попробовать как вопрос");
    }

    [Fact]
    public void ОдинокоеСловоВопросомНеСчитается()
    {
        Assert.False(Conversation.MightBeTalk("как"));
        Assert.False(Conversation.MightBeTalk("что"));
    }

    [Fact]
    public void ДлиннаяФразаСкорееРечьЧемНазваниеПрограммы()
    {
        Assert.True(Conversation.MightBeTalk("мне тут стало интересно можно ли вообще так делать вообще"));
    }
}

/// <summary>
/// Текст, написанный для глаз, и текст для произнесения — разные вещи.
/// Здесь проверяется превращение первого во второе.
/// </summary>
public class SpeechTextTests
{
    [Fact]
    public void РазметкаНеПроизносится()
    {
        var spoken = SpeechText.ForSpeaking("**Steam** — это `магазин` игр");

        Assert.DoesNotContain('*', spoken);
        Assert.DoesNotContain('`', spoken);
        Assert.Contains("Steam", spoken);
        Assert.Contains("магазин", spoken);
    }

    [Fact]
    public void СписокПревращаетсяВСвязнуюРечь()
    {
        var spoken = SpeechText.ForSpeaking("Варианты:\n- первый\n- второй");

        Assert.DoesNotContain("- ", spoken);
        Assert.Contains("первый", spoken);
        Assert.Contains("второй", spoken);
    }

    [Fact]
    public void ЗаголовкиИСмайликиУбираются()
    {
        var spoken = SpeechText.ForSpeaking("## Итог 🎉\nГотово");

        Assert.DoesNotContain('#', spoken);
        Assert.DoesNotContain("🎉", spoken);
        Assert.Contains("Итог", spoken);
    }

    [Fact]
    public void ИзОднихЗнаковПрепинанияПроизноситьНечего()
    {
        Assert.Equal("", SpeechText.ForSpeaking("***"));
        Assert.Equal("", SpeechText.ForSpeaking("   "));
    }

    [Fact]
    public void ЗаконченноеПредложениеОтдаётсяДоКонцаОтвета()
    {
        var buffer = new StringBuilder("Сейчас в Москве около двадцати градусов. А вот дальше");

        var first = SpeechText.TakeSpeakable(buffer);

        Assert.NotNull(first);
        Assert.Contains("двадцати градусов", first);

        // Незаконченный хвост остаётся ждать продолжения.
        Assert.Equal("А вот дальше", buffer.ToString().Trim());
    }

    [Fact]
    public void НезаконченноеПредложениеЖдёт()
    {
        var buffer = new StringBuilder("Сейчас в Москве около");
        Assert.Null(SpeechText.TakeSpeakable(buffer));
    }

    [Fact]
    public void ТочкаВЧислеКонцомФразыНеСчитается()
    {
        var buffer = new StringBuilder("Это будет стоить 1.500 рублей примерно");
        Assert.Null(SpeechText.TakeSpeakable(buffer));
    }

    [Fact]
    public void ОстатокОтдаётсяПоЗавершении()
    {
        var buffer = new StringBuilder("хвост без точки");

        Assert.Null(SpeechText.TakeSpeakable(buffer));
        Assert.Equal("хвост без точки", SpeechText.TakeSpeakable(buffer, flush: true));
    }

    [Fact]
    public void ДлиннаяФразаБезТочкиВсёРавноНачинаетГовориться()
    {
        // Иначе человек ждёт конца абзаца, слушая тишину.
        var text = string.Join(", ", Enumerable.Repeat("довольно длинный кусок текста", 12));
        var buffer = new StringBuilder(text);

        Assert.NotNull(SpeechText.TakeSpeakable(buffer));
    }

    [Fact]
    public void ОтветРазбиваетсяНаПроизносимыеКуски()
    {
        var parts = SpeechText.Split("Первое предложение здесь. Второе предложение тоже. И третье");

        Assert.Equal(3, parts.Count);
        Assert.Contains("третье", parts[^1]);
    }
}
