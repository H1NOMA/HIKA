using System.Text.Json;
using Hika.Config;
using Xunit;

namespace Hika.Tests;

/// <summary>
/// Значения по умолчанию — это решения, а не случайность, и меняются они
/// молча. Здесь закреплены те, за которые уже пришлось расплатиться
/// в переписке с человеком: если кто-то передумает, тест скажет об этом
/// раньше, чем скажет пользователь.
/// </summary>
public class ConfigDefaultsTests
{
    [Fact]
    public void СвечениеНеПоявляетсяДоТогоКакУслышаноИмя()
    {
        // Задумывалось как вежливость: видно, что тебя услышали, ещё до того,
        // как имя распозналось. На деле кайма вспыхивала от любого звука
        // в комнате и переставала что-либо значить.
        Assert.False(new OverlayConfig().ShowBeforeWakeWord);
    }

    [Fact]
    public void РазговорВыключенПокаНетКлюча()
    {
        // Он платный и уходит в интернет. Такое не включают за человека.
        Assert.False(new BrainConfig().Enabled);
    }

    [Fact]
    public void ГолосНеЛезетВИнтернетПоСобственнойИнициативе()
    {
        // «auto» ищет нейроголос, установленный в самой Windows,
        // и не отправляет произносимый текст на чужие серверы.
        Assert.Equal("auto", new VoiceConfig().Engine);
    }

    [Fact]
    public void ЗапускПрограммНеКомментируетсяВслух()
    {
        Assert.False(new VoiceConfig().SpeakConfirmations);
        Assert.True(new VoiceConfig().SuppressMicWhileSpeaking);
    }

    [Fact]
    public void РазделыНастроекДосыпаютсяПриЧтенииНеполногоФайла()
    {
        // Файл после обновления не содержит новых разделов вовсе —
        // и это не должно кончаться падением на первом же обращении.
        var path = Path.Combine(Path.GetTempPath(), $"hika-config-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "persona": "avi" }""");

        try
        {
            var config = new ConfigStore(path).Load();

            Assert.NotNull(config.Voice);
            Assert.NotNull(config.Brain);
            Assert.NotNull(config.Learning);
            Assert.Equal("avi", config.Persona);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void НовыеРазделыПопадаютВСохранённыйФайл()
    {
        var config = new HikaConfig();
        var json = JsonSerializer.Serialize(config);

        Assert.Contains("\"Voice\"", json);
        Assert.Contains("\"Brain\"", json);
        Assert.Contains("\"Learning\"", json);
    }
}

/// <summary>
/// Файл настроек человек открывает и правит руками — я сама его об этом прошу.
/// А раз так, в нём заводится то, чего в коде нет: строка на будущее, ключ
/// из более новой сборки, пометка себе. Первое же «Применить» стирало всё
/// это без следа и без слова.
/// </summary>
public class ЧужиеКлючиВНастройкахTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Записать и прочитать обратно — как это делает ConfigStore.</summary>
    private static HikaConfig Кругооборот(string json)
    {
        var первый = JsonSerializer.Deserialize<HikaConfig>(json, Options)!;
        return JsonSerializer.Deserialize<HikaConfig>(JsonSerializer.Serialize(первый, Options), Options)!;
    }

    [Fact]
    public void ЧужойКлючВКорнеПереживаетСохранение()
    {
        var config = Кругооборот("""
            { "persona": "hika", "мояПометка": "не трогать громкость" }
            """);

        Assert.NotNull(config.Unknown);
        Assert.Equal("не трогать громкость", config.Unknown!["мояПометка"].GetString());
    }

    [Fact]
    public void ЧужойКлючВРазделеПереживаетСохранение()
    {
        var config = Кругооборот("""
            { "audio": { "gain": 2.0, "будущаяНастройка": 42 } }
            """);

        Assert.Equal(2.0f, config.Audio.Gain);
        Assert.NotNull(config.Audio.Unknown);
        Assert.Equal(42, config.Audio.Unknown!["будущаяНастройка"].GetInt32());
    }

    [Fact]
    public void СвоиКлючиНеУезжаютВЧужие()
    {
        var config = Кругооборот("""{ "audio": { "gain": 2.0 } }""");

        Assert.Null(config.Unknown);
        Assert.Null(config.Audio.Unknown);
    }
}

/// <summary>
/// Свои команды: то, что человек вписал в окне настроек, должно доехать
/// до каталога и вернуться обратно в окно без потерь.
/// </summary>
public class СвоиКомандыTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void ЗаписьСНесколькимиФразамиГодится()
    {
        var entry = new CustomEntry
        {
            Phrases = new List<string> { "открой мою папку", "мои файлы" },
            Target = @"C:\Работа",
        };

        Assert.True(entry.IsValid);
    }

    [Fact]
    public void БезЦелиЗаписьНеГодится()
        => Assert.False(new CustomEntry { Phrases = new List<string> { "открой" } }.IsValid);

    [Fact]
    public void БезФразыЗаписьНеГодится()
        => Assert.False(new CustomEntry { Target = "notepad.exe" }.IsValid);

    /// <summary>
    /// Аргументы командной строки в окне настроек не показываются: их правят
    /// руками. Значит, сохранение из окна обязано их пронести — иначе первое
    /// же «Применить» сотрёт их у того самого человека, который не поленился
    /// их вписать.
    /// </summary>
    [Fact]
    public void АргументыПереживаютСохранение()
    {
        var json = """
            { "custom": [ { "phrases": ["открой смету"], "target": "excel.exe", "arguments": "смета.xlsx" } ] }
            """;

        var config = JsonSerializer.Deserialize<HikaConfig>(json, Options)!;
        var снова = JsonSerializer.Deserialize<HikaConfig>(
            JsonSerializer.Serialize(config, Options), Options)!;

        Assert.Single(снова.Custom);
        Assert.Equal("смета.xlsx", снова.Custom[0].Arguments);
    }
}
