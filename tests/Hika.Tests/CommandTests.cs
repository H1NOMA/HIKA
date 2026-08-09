using Hika.Catalog;
using Hika.Config;
using Hika.Nlu;
using Hika.Stt;
using Xunit;

namespace Hika.Tests;

public class CommandParserTests
{
    [Theory]
    [InlineData("открой ютуб", "ютуб")]
    [InlineData("запусти телеграм", "телеграм")]
    [InlineData("включи спотифай", "спотифай")]
    [InlineData("ютуб", "ютуб")]
    [InlineData("open word", "word")]
    [InlineData("launch chrome", "chrome")]
    [InlineData("открой пожалуйста ютуб", "ютуб")]
    [InlineData("открой сайт ютуб", "ютуб")]
    public void СнимаетГлаголИОставляетЦель(string command, string expectedTarget)
    {
        var intent = CommandParser.Parse(command);

        Assert.Equal(IntentKind.Launch, intent.Kind);
        Assert.Equal(expectedTarget, intent.Argument);
    }

    [Theory]
    // Живая речь, а не диктовка роботу. Именно так люди и просят.
    [InlineData("открой-ка мне стим", "стим")]
    [InlineData("открой ка мне steam", "steam")]
    [InlineData("можешь открыть стим", "стим")]
    [InlineData("не мог бы ты открыть мне ютуб", "ютуб")]
    [InlineData("будь добр запусти телеграм", "телеграм")]
    [InlineData("мне нужно открыть ворд", "ворд")]
    [InlineData("я хочу открыть ютуб", "ютуб")]
    [InlineData("а открой-ка ютуб", "ютуб")]
    [InlineData("давай быстренько открой стим", "стим")]
    [InlineData("ну-ка запусти мне дискорд", "дискорд")]
    [InlineData("can you open steam", "steam")]
    public void ПониматьЖивуюРечь(string command, string expectedTarget)
    {
        var intent = CommandParser.Parse(command);

        Assert.Equal(IntentKind.Launch, intent.Kind);
        Assert.Equal(expectedTarget, intent.Argument);
    }

    [Theory]
    [InlineData("громче", IntentKind.VolumeUp)]
    [InlineData("сделай громче", IntentKind.VolumeUp)]
    [InlineData("тише", IntentKind.VolumeDown)]
    [InlineData("убавь громкость", IntentKind.VolumeDown)]
    [InlineData("выключи звук", IntentKind.VolumeMute)]
    [InlineData("пауза", IntentKind.MediaPause)]
    [InlineData("следующий трек", IntentKind.MediaNext)]
    [InlineData("сверни всё", IntentKind.ShowDesktop)]
    [InlineData("покажи рабочий стол", IntentKind.ShowDesktop)]
    [InlineData("закрой окно", IntentKind.CloseWindow)]
    [InlineData("заблокируй компьютер", IntentKind.LockWorkstation)]
    [InlineData("скриншот", IntentKind.Screenshot)]
    public void УзнаётГотовыеКоманды(string command, IntentKind expected)
    {
        Assert.Equal(expected, CommandParser.Parse(command).Kind);
    }

    [Theory]
    [InlineData("найди рецепт борща", "рецепт борща")]
    [InlineData("загугли погоду", "погоду")]
    [InlineData("найди в гугле котиков", "котиков")]
    public void ОтправляетВПоиск(string command, string expectedQuery)
    {
        var intent = CommandParser.Parse(command);

        Assert.Equal(IntentKind.Search, intent.Kind);
        Assert.Equal(expectedQuery, intent.Argument);
    }

    [Fact]
    public void ГотовыеКомандыНеПерехватываютЗапуск()
    {
        // «Пауза» — готовая команда, но «открой паузу» ей быть не должно.
        Assert.Equal(IntentKind.Launch, CommandParser.Parse("открой обс студио").Kind);
        Assert.Equal(IntentKind.Launch, CommandParser.Parse("запусти диспетчер задач").Kind);
    }

    [Theory]
    // «Запусти» снимает неоднозначность: человек говорит о программе,
    // и в поиск такая команда уходить не должна ни при каких обстоятельствах.
    [InlineData("запусти халдайверс два")]
    [InlineData("открой стим")]
    [InlineData("включи обс")]
    public void ЯвныйГлаголОтмечается(string command)
    {
        var intent = CommandParser.Parse(command);

        Assert.Equal(IntentKind.Launch, intent.Kind);
        Assert.True(intent.ExplicitVerb, $"«{command}» — глагол не отмечен как явный");
    }

    [Fact]
    public void БезГлаголаПризнакаНет()
    {
        Assert.False(CommandParser.Parse("ютуб").ExplicitVerb);
    }

    [Fact]
    public void ГлаголБезЦелиНеДаётНамерения()
    {
        Assert.Equal(IntentKind.None, CommandParser.Parse("открой").Kind);
    }

    [Fact]
    public void ОдинокоеСловоГуглОткрываетСайтАНеИщетПустоту()
    {
        var intent = CommandParser.Parse("гугл");
        Assert.Equal(IntentKind.Launch, intent.Kind);
    }
}

public class CatalogTests
{
    private static AppCatalog Catalog()
    {
        var catalog = new AppCatalog();
        catalog.Load(new HikaConfig());
        return catalog;
    }

    [Fact]
    public void ВстроенныйКаталогЗагрузился()
    {
        var catalog = Catalog();
        Assert.True(catalog.BuiltinCount > 50, $"во встроенном каталоге всего {catalog.BuiltinCount} записей");
    }

    [Theory]
    [InlineData("ютуб", "youtube")]
    [InlineData("ютьюб", "youtube")]
    [InlineData("youtube", "youtube")]
    [InlineData("хром", "chrome")]
    [InlineData("гугл хром", "chrome")]
    [InlineData("ворд", "word")]
    [InlineData("word", "word")]
    [InlineData("эксель", "excel")]
    [InlineData("телеграм", "telegram")]
    [InlineData("телега", "telegram")]
    [InlineData("блокнот", "notepad")]
    [InlineData("калькулятор", "calculator")]
    [InlineData("проводник", "explorer")]
    [InlineData("настройки", "settings")]
    [InlineData("фотошоп", "photoshop")]
    [InlineData("дискорд", "discord")]
    [InlineData("стим", "steam")]
    [InlineData("вконтакте", "vk")]
    [InlineData("вк", "vk")]
    [InlineData("кинопоиск", "kinopoisk")]
    [InlineData("чат гпт", "chatgpt")]
    [InlineData("переводчик", "translate")]
    public void НаходитНужное(string spoken, string expectedId)
    {
        var match = Catalog().Resolve(spoken, new BehaviorConfig().MatchThreshold);

        Assert.NotNull(match);
        Assert.Equal(expectedId, match!.Entry.Id);
    }

    [Theory]
    // Чепуха не должна ни во что превращаться: лучше уйти в поиск,
    // чем открыть наугад что-то постороннее.
    [InlineData("абракадабра")]
    [InlineData("холодильник")]
    [InlineData("расскажи анекдот")]
    public void НаЧепухеМолчит(string spoken)
    {
        Assert.Null(Catalog().Resolve(spoken, new BehaviorConfig().MatchThreshold));
    }
}

public class HallucinationTests
{
    [Theory]
    // Устойчивые выдумки Whisper на тишине. Без этого фильтра ассистент
    // оживал бы от кашля и хлопнувшей двери.
    [InlineData("Субтитры сделал DimaTorzok")]
    [InlineData("Продолжение следует...")]
    [InlineData("Спасибо за просмотр!")]
    [InlineData("Редактор субтитров А.Синецкая Корректор А.Егорова")]
    [InlineData("Thank you.")]
    [InlineData("Thanks for watching!")]
    [InlineData("[музыка]")]
    [InlineData("(шум ветра)")]
    [InlineData("...")]
    [InlineData("ааааааа")]
    [InlineData("")]
    public void ОтсекаетВыдумки(string text)
    {
        Assert.True(Hallucinations.IsLikelyHallucination(text), $"«{text}» прошло как настоящая речь");
    }

    [Theory]
    [InlineData("Ави, открой ютуб")]
    [InlineData("Хика запусти ворд")]
    [InlineData("сделай громче")]
    public void НастоящуюРечьПропускает(string text)
    {
        Assert.False(Hallucinations.IsLikelyHallucination(text), $"«{text}» отбросило как выдумку");
    }

    [Fact]
    public void УбираетПометкиНоОставляетРечь()
    {
        Assert.Equal("Ави открой ютуб", Hallucinations.Clean("[шум] Ави открой ютуб"));
    }
}
