using Hika.Config;
using Hika.Diagnostics;
using Xunit;

namespace Hika.Tests;

/// <summary>
/// Самоизмерение скорости и советы по ней.
///
/// Проверять это тестами важнее, чем кажется. Числа, которые программа
/// показывает человеку, он не может перепроверить: ошибётся медиана — и он
/// будет полдня крутить не тот ползунок, доверяя тому, что написано.
/// Соврать здесь хуже, чем промолчать.
/// </summary>
public class SpeedTests
{
    private static SpeedSample Sample(int silence, int recognition, int action, int wake = 0, int audio = 1000)
        => new()
        {
            SilenceMs = silence,
            RecognitionMs = recognition,
            ActionMs = action,
            WakeMs = wake,
            AudioMs = audio,
        };

    [Fact]
    public void ПокаНичегоНеБылоИзмерятьНечего()
    {
        Assert.Null(new SpeedLog().Summary());
        Assert.Null(new SpeedLog().Last);
    }

    [Fact]
    public void МедианаНеСъезжаетОтОдногоВыброса()
    {
        // Одна команда, попавшая на переиндексацию программ, займёт восемь
        // секунд. Среднее уехало бы туда, где человек никогда не был.
        var log = new SpeedLog();

        for (int i = 0; i < 6; i++) log.Add(Sample(400, 700, 50));
        log.Add(Sample(400, 8000, 50));

        var summary = log.Summary();

        Assert.NotNull(summary);
        Assert.Equal(700, summary!.Value.RecognitionMs);
    }

    [Fact]
    public void ПомнитТолькоПоследние()
    {
        var log = new SpeedLog();
        for (int i = 0; i < 100; i++) log.Add(Sample(400, 700, 50));

        Assert.True(log.Count <= 24, $"накопилось {log.Count} — очередь не обрезается");
    }

    [Fact]
    public void КомандыБезИмениНеЗанижаютВремяПробуждения()
    {
        // В окне продолжения имя не звучит, и пробуждение там равно нулю.
        // Посчитать эти нули вместе с остальными значило бы показать человеку
        // вдвое меньшее число, чем он видит глазами.
        var log = new SpeedLog();

        log.Add(Sample(400, 700, 50, wake: 500));
        log.Add(Sample(400, 700, 50, wake: 500));
        log.Add(Sample(400, 700, 50, wake: 0));
        log.Add(Sample(400, 700, 50, wake: 0));

        Assert.Equal(500, log.Summary()!.Value.WakeMs);
    }

    [Fact]
    public void ОтношениеКДлинеРечиСчитаетсяПоФразам()
    {
        var log = new SpeedLog();
        log.Add(Sample(400, 1000, 50, audio: 2000));

        Assert.Equal(0.5, log.Summary()!.Value.RealTime, 3);
    }

    // ---- Советы -------------------------------------------------------------

    private static SpeedSummary Summary(int silence, int recognition, int action,
        int wake = 400, double realTime = 0.6, int commands = 10)
        => new()
        {
            Commands = commands,
            SilenceMs = silence,
            RecognitionMs = recognition,
            ActionMs = action,
            WakeMs = wake,
            RealTime = realTime,
        };

    [Fact]
    public void ПоОднойКомандеНеСудят()
    {
        var advice = SpeedAdvice.For(Summary(400, 700, 50, commands: 1), new HikaConfig());

        Assert.Equal(SpeedFix.None, advice.Fix);
        Assert.False(advice.Slow);
    }

    [Fact]
    public void ВыключенныеУскоренияЧинятсяПервыми()
    {
        // Стоят они ничего, а забирают до половины времени распознавания.
        // Предлагать сменить модель, пока выключено это, — предлагать
        // человеку заплатить точностью за то, что бесплатно.
        var config = new HikaConfig();
        config.Speech.AdaptiveContext = false;

        var advice = SpeedAdvice.For(Summary(400, 2000, 50, realTime: 2.0), config);

        Assert.Equal(SpeedFix.RestoreAcceleration, advice.Fix);

        Assert.NotEqual("", advice.Apply(config));
        Assert.True(config.Speech.AdaptiveContext);
        Assert.True(config.Speech.FastDecoding);
    }

    [Fact]
    public void МодельНеУспевающаяЗаРечьюСтановитсяЛегче()
    {
        var config = new HikaConfig();
        config.Speech.Model = "medium";

        var advice = SpeedAdvice.For(Summary(400, 2500, 50, realTime: 2.5), config);

        Assert.Equal(SpeedFix.FasterModel, advice.Fix);
        Assert.True(advice.Slow);

        advice.Apply(config);
        Assert.Equal("small", config.Speech.Model);
    }

    [Fact]
    public void СамойЛёгкойМоделиСоветоватьНечего()
    {
        var config = new HikaConfig();
        config.Speech.Model = "tiny";

        var advice = SpeedAdvice.For(Summary(400, 2500, 50, realTime: 2.5), config);

        // Кнопки здесь быть не должно: настройками отсюда не выбраться,
        // и делать вид, что выбраться можно, — обман.
        Assert.Equal(SpeedFix.NeedsGpu, advice.Fix);
        Assert.False(advice.Actionable);
        Assert.Contains("vulkan", advice.Detail);
    }

    [Fact]
    public void ДлиннаяПаузаПередКонцомФразыЗамечается()
    {
        var config = new HikaConfig();
        config.Audio.SilenceMs = 900;

        var advice = SpeedAdvice.For(Summary(900, 500, 50), config);

        Assert.Equal(SpeedFix.ShorterSilence, advice.Fix);

        advice.Apply(config);
        Assert.Equal(300, config.Audio.SilenceMs);
    }

    [Fact]
    public void ПоздняяКаймаЗамечается()
    {
        var config = new HikaConfig();
        config.Speech.ProbeAfterMs = 900;
        config.Speech.ProbeModel = "small";

        var advice = SpeedAdvice.For(Summary(300, 400, 50, wake: 1300), config);

        Assert.Equal(SpeedFix.FasterProbe, advice.Fix);

        advice.Apply(config);
        Assert.Equal(300, config.Speech.ProbeAfterMs);
        Assert.Equal("tiny", config.Speech.ProbeModel);
    }

    [Fact]
    public void КогдаБыстроНичегоНеСоветуется()
    {
        var advice = SpeedAdvice.For(Summary(300, 500, 40, wake: 400), new HikaConfig());

        Assert.Equal(SpeedFix.None, advice.Fix);
        Assert.False(advice.Slow);
        Assert.Equal("Быстро", advice.Verdict);
    }

    [Fact]
    public void СоветНеПредлагаетТогоЧтоУжеСделано()
    {
        // Настройка уже на трёхстах — предлагать сократить до трёхсот значит
        // показать кнопку, от которой ничего не изменится.
        var config = new HikaConfig();
        config.Audio.SilenceMs = 300;

        var advice = SpeedAdvice.For(Summary(300, 250, 40), config);

        Assert.NotEqual(SpeedFix.ShorterSilence, advice.Fix);
    }

    [Theory]
    [InlineData(120, "120 мс")]
    [InlineData(999, "999 мс")]
    [InlineData(1000, "1,0 с")]
    [InlineData(1450, "1,5 с")]
    [InlineData(2400, "2,4 с")]
    public void ВремяЧитаетсяПоРусски(int ms, string expected)
    {
        Assert.Equal(expected, SpeedAdvice.Text(ms));
    }
}

/// <summary>
/// Ожидание текста и счёт модели — разные числа.
///
/// Раньше «действие» считалось как «всё минус время модели», и очередь
/// с догрузкой уезжали в него. Программа показывала полсекунды действия
/// там, где выполнялось «поставь паузу», и советовала чинить то, чего
/// не происходило.
/// </summary>
public class ОжиданиеИСчётМоделиTests
{
    [Fact]
    public void ОтношениеКРечиСчитаетсяПоСчётуМодели()
    {
        // Фраза длиной секунду. Модель считала триста миллисекунд, а человек
        // ждал две секунды — потому что модель в этот момент догружалась.
        var sample = new SpeedSample
        {
            AudioMs = 1000,
            RecognitionMs = 2000,
            DecodeMs = 300,
        };

        // Модель за речью успевает: 0.3, а не 2.0. Иначе совет был бы
        // «возьмите модель полегче» — при том, что она и так справляется.
        Assert.Equal(0.3, sample.RealTime, 3);
    }

    [Fact]
    public void БезОтдельногоСчётаБерётсяОбщееОжидание()
    {
        var sample = new SpeedSample { AudioMs = 1000, RecognitionMs = 1500 };

        Assert.Equal(1.5, sample.RealTime, 3);
    }

    [Fact]
    public void ОжиданиеТекстаВходитВОбщееВремя()
    {
        var sample = new SpeedSample
        {
            SilenceMs = 400,
            RecognitionMs = 2000,
            DecodeMs = 300,
            ActionMs = 100,
        };

        // Две с половиной секунды — ровно столько человек и прождал.
        // Считать здесь по DecodeMs значило бы обещать 800 мс.
        Assert.Equal(2500, sample.TotalMs);
    }
}
