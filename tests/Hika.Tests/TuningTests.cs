using Hika.Config;
using Hika.Stt;
using Xunit;

namespace Hika.Tests;

/// <summary>
/// Размер окна кодировщика — главный рычаг отзывчивости и одновременно
/// единственное место, где за скорость можно заплатить качеством
/// распознавания. Поэтому границы здесь закреплены явно.
/// </summary>
public class WhisperTuningTests
{
    [Fact]
    public void КороткаяКомандаНеТащитЗаСобойТридцатьСекундТишины()
    {
        // «Ави, открой стим» — две секунды. Полное окно тут означало бы,
        // что двадцать восемь секунд из тридцати уходят на обработку тишины,
        // которую мы сами же и дописали.
        var context = WhisperTuning.AudioContextFor(2.0);

        Assert.True(context < WhisperTuning.FullContext / 4,
            $"окно {context} — экономии почти нет");
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(2.0)]
    [InlineData(5.0)]
    [InlineData(12.0)]
    [InlineData(15.0)]
    public void ОкнаВсегдаХватаетНаСамуРечьСЗапасом(double seconds)
    {
        var context = WhisperTuning.AudioContextFor(seconds);

        // Пятьдесят позиций на секунду — и сверху обязательно тишина,
        // иначе модель не поймёт, что фраза кончилась.
        var needed = seconds * 50;

        Assert.True(context >= needed + 50,
            $"на {seconds} с дали окно {context}, а нужно минимум {needed + 50}");
    }

    [Fact]
    public void ДлиннаяЗаписьПолучаетПолноеОкно()
    {
        Assert.Equal(WhisperTuning.FullContext, WhisperTuning.AudioContextFor(30));
        Assert.Equal(WhisperTuning.FullContext, WhisperTuning.AudioContextFor(120));
    }

    [Fact]
    public void РазмерСчитаетсяИПоКоличествуОтсчётов()
    {
        Assert.Equal(
            WhisperTuning.AudioContextFor(2.0),
            WhisperTuning.AudioContextForSamples(32000));
    }

    [Fact]
    public void РастёмСразуСжимаемсяНеТоропясь()
    {
        // Не хватило места — обрежется речь. Это прямая потеря, ждать нельзя.
        Assert.True(WhisperTuning.ShouldSwitch(current: 320, wanted: 1024, shorterInARow: 0));

        // А вот уменьшаться из-за одной короткой фразы не надо: следующая
        // может снова оказаться длинной, и обвязку придётся пересобирать дважды.
        Assert.False(WhisperTuning.ShouldSwitch(current: 1024, wanted: 320, shorterInARow: 1));
        Assert.True(WhisperTuning.ShouldSwitch(current: 1024, wanted: 320, shorterInARow: 3));

        // Ничего не изменилось — ничего и не пересобираем.
        Assert.False(WhisperTuning.ShouldSwitch(current: 320, wanted: 320, shorterInARow: 9));
    }
}

/// <summary>
/// Перенос старого файла настроек на новые умолчания. Правило одно:
/// трогать только то, чего человек не касался.
/// </summary>
public class MigrationTests
{
    [Fact]
    public void СтарыеУмолчанияПодтягиваютсяКНовым()
    {
        var config = new HikaConfig
        {
            Version = 1,
            Audio = { SilenceMs = 500 },
            Speech = { ProbeAfterMs = 900 },
            Overlay = { Thickness = 0.09 },
        };

        Assert.True(Migrations.Apply(config));

        Assert.Equal(400, config.Audio.SilenceMs);
        Assert.Equal(600, config.Speech.ProbeAfterMs);
        Assert.Equal(0.07, config.Overlay.Thickness, 6);
        Assert.Equal(HikaConfig.CurrentVersion, config.Version);
    }

    [Fact]
    public void ВыбранноеЧеловекомНеТрогаетсяНикогда()
    {
        var config = new HikaConfig
        {
            Version = 1,
            Audio = { SilenceMs = 800 },
            Speech = { ProbeAfterMs = 1200 },
            Overlay = { Thickness = 0.15 },
        };

        Migrations.Apply(config);

        Assert.Equal(800, config.Audio.SilenceMs);
        Assert.Equal(1200, config.Speech.ProbeAfterMs);
        Assert.Equal(0.15, config.Overlay.Thickness, 6);
    }

    [Fact]
    public void ПовторныйЗапускНичегоНеДелает()
    {
        var config = new HikaConfig { Version = HikaConfig.CurrentVersion };
        Assert.False(Migrations.Apply(config));
    }

    [Fact]
    public void ФайлСтарогоОбразцаПереносится()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hika-cfg-{Guid.NewGuid():N}.json");

        // Ровно то, что лежит у человека после первого запуска старой сборки:
        // версии нет вовсе, значения выписаны старыми умолчаниями.
        File.WriteAllText(path, """
            {
              "persona": "avi",
              "audio": { "silenceMs": 500 },
              "speech": { "probeAfterMs": 900 },
              "overlay": { "thickness": 0.09 }
            }
            """);

        try
        {
            var config = new ConfigStore(path).Load();

            Assert.Equal(400, config.Audio.SilenceMs);
            Assert.Equal(600, config.Speech.ProbeAfterMs);
            Assert.Equal("avi", config.Persona);

            // И перенос обязан сохраниться на диск, иначе он повторится
            // при каждом запуске.
            Assert.Contains($"\"Version\": {HikaConfig.CurrentVersion}", File.ReadAllText(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
