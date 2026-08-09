using Hika.Nlu;
using Xunit;

namespace Hika.Tests;

/// <summary>
/// Числа, названные словами. Без них не работают ни громкость, ни таймеры:
/// распознаватель отдаёт «тридцать», а не «30».
/// </summary>
public class NumbersTests
{
    [Theory]
    [InlineData("пять", 5)]
    [InlineData("двадцать", 20)]
    [InlineData("сто", 100)]
    [InlineData("15", 15)]
    public void ПростыеЧислаЧитаются(string word, int expected)
    {
        Assert.Equal(expected, Numbers.First(new[] { word }));
    }

    [Fact]
    public void СоставныеЧислаСкладываются()
    {
        Assert.Equal(25, Numbers.First(TextNormalizer.Tokenize("двадцать пять")));
        Assert.Equal(125, Numbers.First(TextNormalizer.Tokenize("сто двадцать пять")));
    }

    [Fact]
    public void РазрядНеУбываетЗначитЧислоКончилось()
    {
        // «Два три» — это два числа подряд, а не двадцать три.
        Assert.Equal(2, Numbers.Read(TextNormalizer.Tokenize("два три"), 0, out var consumed));
        Assert.Equal(1, consumed);
    }

    [Fact]
    public void ЧиселНетЗначитNull()
    {
        Assert.Null(Numbers.First(TextNormalizer.Tokenize("открой стим")));
    }

    [Theory]
    [InlineData("пять минут", 300)]
    [InlineData("десять секунд", 10)]
    [InlineData("два часа", 7200)]
    [InlineData("полчаса", 1800)]
    public void ПромежуткиВремениЧитаются(string text, int seconds)
    {
        var duration = Numbers.Duration(TextNormalizer.Tokenize(text));

        Assert.NotNull(duration);
        Assert.Equal(seconds, (int)duration!.Value.TotalSeconds);
    }

    [Fact]
    public void БезЕдиницыИзмеренияЭтоМинуты()
    {
        // «Поставь таймер на пять» — пять минут. Не секунд и не часов:
        // секунды слишком мало, чтобы о них просить, а часы назвали бы прямо.
        var duration = Numbers.Duration(TextNormalizer.Tokenize("таймер на пять"));

        Assert.Equal(TimeSpan.FromMinutes(5), duration);
    }
}

/// <summary>
/// Живая речь: команды с числом внутри, несколько просьб в одном предложении
/// и разница между «пауза» и «продолжи».
/// </summary>
public class FreeSpeechTests
{
    [Theory]
    [InlineData("сделай громкость тридцать", 30)]
    [InlineData("громкость на 50 процентов", 50)]
    [InlineData("поставь звук двадцать пять", 25)]
    public void ГромкостьПонимаетсяЧислом(string text, int expected)
    {
        var intent = CommandParser.Parse(text);

        Assert.Equal(IntentKind.VolumeSet, intent.Kind);
        Assert.Equal(expected.ToString(), intent.Argument);
    }

    [Fact]
    public void ГромкостьБезЧислаОстаётсяОбычнойКомандой()
    {
        Assert.NotEqual(IntentKind.VolumeSet, CommandParser.Parse("сделай громче").Kind);
        Assert.NotEqual(IntentKind.VolumeSet, CommandParser.Parse("убавь звук").Kind);
    }

    [Theory]
    [InlineData("поставь таймер на пять минут", 300)]
    [InlineData("напомни через десять минут", 600)]
    [InlineData("засеки полчаса", 1800)]
    public void ТаймерПонимаетПромежуток(string text, int seconds)
    {
        var intent = CommandParser.Parse(text);

        Assert.Equal(IntentKind.Timer, intent.Kind);
        Assert.Equal(seconds.ToString(), intent.Argument);
    }

    [Theory]
    [InlineData("переключись на хром", "хром")]
    [InlineData("перейди в телеграм", "телеграм")]
    [InlineData("разверни ворд", "ворд")]
    public void ПереключениеНаОкноОтличаетсяОтЗапуска(string text, string target)
    {
        var intent = CommandParser.Parse(text);

        Assert.Equal(IntentKind.FocusWindow, intent.Kind);
        Assert.Equal(target, intent.Argument);
    }

    [Theory]
    // Сказанное вслух «пауза» означает «пусть замолчит». Включить музыку
    // в ответ — ровно противоположное тому, о чём просили.
    [InlineData("пауза", IntentKind.MediaPause)]
    [InlineData("стоп", IntentKind.MediaPause)]
    [InlineData("останови", IntentKind.MediaPause)]
    [InlineData("продолжи", IntentKind.MediaPlay)]
    [InlineData("что играет", IntentKind.NowPlaying)]
    [InlineData("который час", IntentKind.Time)]
    public void УзнаётНовыеКоманды(string text, IntentKind expected)
    {
        Assert.Equal(expected, CommandParser.Parse(text).Kind);
    }

    [Fact]
    public void ОборванныйГлаголНеПревращаетсяВКоманду()
    {
        // «Открой» без цели совпадало с «открой вкладку» и открывало вкладку
        // браузера — одно слово не должно заменять двухсловную команду.
        Assert.Equal(IntentKind.None, CommandParser.Parse("открой").Kind);
    }

    [Fact]
    public void ПредложениеДелитсяНаКоманды()
    {
        var parts = CommandParser.Segments("открой стим и сделай потише");

        Assert.Equal(2, parts.Count);
        Assert.Contains("стим", parts[0]);
        Assert.Contains("потише", parts[1]);
    }

    [Theory]
    [InlineData("открой ютуб")]
    [InlineData("сделай тише")]
    [InlineData("запусти стим")]
    public void КороткиеФразыНеРежутся(string text)
    {
        Assert.Single(CommandParser.Segments(text));
    }

    [Fact]
    public void РазборТолькоПредлагаетМестаРазреза()
    {
        // «Гарри Поттер и узник Азкабана» здесь разделится — по одному тексту
        // отличить название от двух команд невозможно. Окончательное решение
        // принимается там, где известен каталог: если хоть одна часть
        // не находится среди программ, фраза остаётся целой.
        //
        // Тест закрепляет именно это разделение обязанностей: разбор языка
        // не должен делать вид, что знает, что установлено на компьютере.
        var parts = CommandParser.Segments("гарри поттер и узник азкабана");

        Assert.Equal(2, parts.Count);
        Assert.All(parts, p => Assert.NotEmpty(p));
    }

    [Fact]
    public void СоюзВНачалеРазделителемНеСчитается()
    {
        // «А открой мне стим» — затравка, а не две команды.
        Assert.Single(CommandParser.Segments("а открой мне стим"));
    }
}

/// <summary>
/// Конструктор фраз: команда описана устройством, а не перечислением.
///
/// Смысл проверок ниже — не в конкретных фразах, а в том, что работают
/// и те сочетания, которых никто не выписывал. Заготовленный список
/// такого не даёт по определению: шестую формулировку человек всё равно
/// скажет мимо него.
/// </summary>
public class PhrasePatternTests
{
    [Theory]
    // Ровно то, о чём просили: односложные, живые формы.
    [InlineData("следующая", IntentKind.MediaNext)]
    [InlineData("следующий", IntentKind.MediaNext)]
    [InlineData("дальше", IntentKind.MediaNext)]
    [InlineData("переключи", IntentKind.MediaNext)]
    [InlineData("следующий трек", IntentKind.MediaNext)]
    [InlineData("давай следующую песню", IntentKind.MediaNext)]

    [InlineData("продолжай", IntentKind.MediaPlay)]
    [InlineData("продолжи", IntentKind.MediaPlay)]
    [InlineData("продолжай музыку", IntentKind.MediaPlay)]
    [InlineData("возобнови воспроизведение", IntentKind.MediaPlay)]

    [InlineData("стоп", IntentKind.MediaPause)]
    [InlineData("хватит", IntentKind.MediaPause)]
    [InlineData("поставь паузу", IntentKind.MediaPause)]
    [InlineData("останови музыку", IntentKind.MediaPause)]

    [InlineData("предыдущая", IntentKind.MediaPrevious)]
    [InlineData("верни предыдущий трек", IntentKind.MediaPrevious)]

    // Сочетания глагола и предмета, которые не выписаны нигде.
    [InlineData("включи музыку", IntentKind.PlayMusic)]
    [InlineData("врубай музон", IntentKind.PlayMusic)]
    [InlineData("запусти мою музыку", IntentKind.PlayMusic)]
    [InlineData("поставь мой плейлист", IntentKind.PlayMusic)]
    [InlineData("давай любимую музыку", IntentKind.PlayMusic)]
    [InlineData("музыку", IntentKind.PlayMusic)]

    [InlineData("что играет", IntentKind.NowPlaying)]
    [InlineData("какая песня", IntentKind.NowPlaying)]
    [InlineData("а что сейчас играет", IntentKind.NowPlaying)]

    [InlineData("погромче", IntentKind.VolumeUp)]
    [InlineData("прибавь звук", IntentKind.VolumeUp)]
    [InlineData("сделай потише", IntentKind.VolumeDown)]
    [InlineData("приглуши музыку", IntentKind.VolumeDown)]
    public void СочетанияРаботаютБезПеречисления(string text, IntentKind expected)
    {
        Assert.Equal(expected, CommandParser.Parse(text).Kind);
    }

    [Theory]
    // Лишнее слово означает другую команду. Иначе «включи музыку в стиме»
    // включало бы музыку вместо того, чтобы открыть Steam.
    [InlineData("включи музыку в стиме")]
    [InlineData("открой стим")]
    [InlineData("запусти халдайверс два")]
    [InlineData("включи телеграм")]
    [InlineData("поставь фотошоп")]
    public void ЗапускПрограммыШаблонамиНеПерехватывается(string text)
    {
        var kind = CommandParser.Parse(text).Kind;

        Assert.True(kind is IntentKind.Launch or IntentKind.None,
            $"«{text}» разобралось как {kind}, а должно было остаться запуском");
    }

    [Fact]
    public void ДальшеЭтоСледующийТрекАНеПродолжение()
    {
        // Человек сказал прямо: «дальше» — это следующая песня.
        // Слово встречается и в описании продолжения, и разводить их
        // приходится порядком, а не порогами.
        Assert.Equal(IntentKind.MediaNext, CommandParser.Parse("дальше").Kind);
    }

    [Fact]
    public void НеобязательныйСлотНеОтбираетСловоУОбязательного()
    {
        // «Включи следующий» — «включи» подходит и необязательному слоту
        // переключения, и обязательному слоту продолжения. Жадный разбор
        // отдал бы слово первому и потерял команду.
        Assert.Equal(IntentKind.MediaNext, CommandParser.Parse("включи следующий").Kind);
    }
}
