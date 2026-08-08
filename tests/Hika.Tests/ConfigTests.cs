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
