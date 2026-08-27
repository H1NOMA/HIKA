using Hika.Config;

namespace Hika.Diagnostics;

/// <summary>Что именно предлагается сделать, чтобы стало быстрее.</summary>
public enum SpeedFix
{
    /// <summary>Ничего: либо всё хорошо, либо чинить надо не настройками.</summary>
    None,

    /// <summary>Включить обратно ускорения распознавания, которые кто-то выключил.</summary>
    RestoreAcceleration,

    /// <summary>Взять модель полегче.</summary>
    FasterModel,

    /// <summary>Меньше ждать тишины перед тем, как счесть фразу законченной.</summary>
    ShorterSilence,

    /// <summary>Раньше и легче проверять имя.</summary>
    FasterProbe,

    /// <summary>Настройками уже не помочь — нужна сборка с ускорением на видеокарте.</summary>
    NeedsGpu,
}

/// <summary>
/// Приговор скорости и одна кнопка, которая его исправляет.
///
/// Существует потому, что вопрос «почему медленно» человек задаёт программе,
/// а не себе. Отвечать на него числами и оставлять человека наедине с девятью
/// ползунками — это переложить свою работу на того, кто заведомо не знает,
/// какой из них тут виноват. Программа знает: она видит, из чего сложилось
/// ожидание, и назвать виноватого может сама.
///
/// Правила нарочно выстроены по одной цене за другой: сначала то, что ничего
/// не стоит (выключенные ускорения), потом то, что стоит точности (модель
/// полегче), и только в конце — то, что настройками уже не лечится.
/// </summary>
public sealed record SpeedAdvice(string Verdict, string Detail, SpeedFix Fix, string FixLabel)
{
    /// <summary>
    /// Есть ли что нажать.
    ///
    /// «Нужна видеокарта» — не кнопка: нажимать там нечего, и показать кнопку,
    /// от которой ничего не происходит, хуже, чем не показать никакой.
    /// </summary>
    public bool Actionable => Fix is not (SpeedFix.None or SpeedFix.NeedsGpu);

    /// <summary>Медленно настолько, что об этом стоит сказать вслух.</summary>
    public bool Slow { get; init; }

    public static SpeedAdvice For(SpeedSummary summary, HikaConfig config)
    {
        var total = summary.TotalMs;
        var model = (config.Speech.Model ?? "small").Trim().ToLowerInvariant();

        if (summary.Commands < 3)
        {
            return new SpeedAdvice(
                "Ещё считаю",
                $"Скажите несколько команд — по одной-двум судить рано. Пока их {summary.Commands}.",
                SpeedFix.None, "");
        }

        // Самое дешёвое из возможного: ускорения, выключенные руками. Стоят они
        // ничего, а забирают до половины времени распознавания.
        if (!config.Speech.AdaptiveContext || !config.Speech.FastDecoding)
        {
            return new SpeedAdvice(
                "Выключены ускорения распознавания",
                "«Окно под длину фразы» и «быстрое декодирование» выключены. Первое — самая крупная " +
                "экономия во всей программе: без него whisper считает тридцать секунд тишины на каждую " +
                "команду из трёх слов. Качество от них почти не зависит.",
                SpeedFix.RestoreAcceleration, "Включить обратно") { Slow = true };
        }

        // Модель, считающая дольше, чем длится сама речь. Это не «медленно» —
        // это «дальше будет хуже»: ожидание растёт вместе с длиной фразы.
        if (summary.RealTime > 1.15 || summary.RecognitionMs > 1400)
        {
            var lighter = Lighter(model);

            if (lighter is null)
            {
                return new SpeedAdvice(
                    "Процессору тяжело",
                    $"Распознавание идёт в {summary.RealTime:0.0} раза дольше, чем длится сама речь, " +
                    "а модель уже самая лёгкая. Настройками отсюда не выбраться: помогла бы сборка " +
                    "с ускорением на видеокарте — hika-win-x64-vulkan.",
                    SpeedFix.NeedsGpu, "") { Slow = true };
            }

            return new SpeedAdvice(
                "Модель не успевает за речью",
                $"Распознавание идёт в {summary.RealTime:0.0} раза дольше, чем длится сама речь. " +
                $"Модель «{lighter}» считает заметно быстрее — русский станет чуть грубее, " +
                "но ответ перестанет отставать. Если точность важнее, возьмите сборку " +
                "с ускорением на видеокарте: она и быстрее, и точнее.",
                SpeedFix.FasterModel, $"Перейти на «{lighter}»") { Slow = true };
        }

        // Ожидание конца фразы — чистая пауза перед началом работы. Когда оно
        // больше самого распознавания, ускорять распознавание бессмысленно.
        if (summary.SilenceMs >= 400 && summary.SilenceMs >= summary.RecognitionMs && config.Audio.SilenceMs > 300)
        {
            return new SpeedAdvice(
                "Дольше всего ждём вашей паузы",
                $"Из {Text(total)} ожидания {Text(summary.SilenceMs)} уходит на то, чтобы убедиться, " +
                "что вы договорили. Это чистая пауза перед началом работы. Триста миллисекунд " +
                "заметно живее и всё ещё длиннее паузы между словами.",
                SpeedFix.ShorterSilence, "Сократить до 300 мс");
        }

        // Кайма, вспыхивающая через секунду после имени, читается как «не услышала».
        if (summary.WakeMs > 800 && (config.Speech.ProbeAfterMs > 300 || IsHeavy(config.Speech.ProbeModel)))
        {
            return new SpeedAdvice(
                "Кайма загорается поздно",
                $"От имени до свечения проходит {Text(summary.WakeMs)}. За это время человек успевает " +
                "решить, что его не услышали, и повторить. Проверять имя можно раньше и моделью полегче — " +
                "она отвечает на единственный вопрос, и большая для этого не нужна.",
                SpeedFix.FasterProbe, "Проверять имя раньше");
        }

        if (total <= 1100)
        {
            return new SpeedAdvice(
                "Быстро",
                $"После того как вы договорили, проходит {Text(total)}. Быстрее без видеокарты не будет.",
                SpeedFix.None, "");
        }

        return new SpeedAdvice(
            "Приемлемо",
            $"После того как вы договорили, проходит {Text(total)}. Крупных запасов не осталось: " +
            "дальше помогает только более лёгкая модель или ускорение на видеокарте.",
            SpeedFix.None, "");
    }

    /// <summary>
    /// Применяет предложенное. Возвращает то, что можно показать человеку,
    /// или пустую строку, если менять было нечего.
    /// </summary>
    public string Apply(HikaConfig config)
    {
        switch (Fix)
        {
            case SpeedFix.RestoreAcceleration:
                config.Speech.AdaptiveContext = true;
                config.Speech.FastDecoding = true;
                return "Ускорения распознавания включены";

            case SpeedFix.FasterModel:
                var lighter = Lighter((config.Speech.Model ?? "small").Trim().ToLowerInvariant());
                if (lighter is null) return "";

                config.Speech.Model = lighter;
                return $"Модель переключена на «{lighter}». Применится после перезапуска";

            case SpeedFix.ShorterSilence:
                config.Audio.SilenceMs = 300;
                return "Пауза до конца фразы — 300 мс";

            case SpeedFix.FasterProbe:
                config.Speech.ProbeAfterMs = 300;
                config.Speech.EarlyWakeProbe = true;
                if (IsHeavy(config.Speech.ProbeModel)) config.Speech.ProbeModel = "tiny";
                return "Имя проверяется раньше и моделью полегче";

            default:
                return "";
        }
    }

    /// <summary>Следующая модель вниз по тяжести. Null — легче уже некуда.</summary>
    private static string? Lighter(string model) => model switch
    {
        "largev3turbo" or "large" or "largev3" => "small",
        "medium" => "small",
        "small" => "base",
        _ => null,
    };

    /// <summary>Модель, которой проверять одно слово — расточительство.</summary>
    private static bool IsHeavy(string? model)
    {
        var name = (model ?? "").Trim().ToLowerInvariant();
        return name is "small" or "medium" or "large" or "largev3" or "largev3turbo";
    }

    /// <summary>Миллисекунды так, как их произносит человек.</summary>
    public static string Text(int ms)
        => ms >= 1000
            ? (ms / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                .Replace('.', ',') + " с"
            : $"{ms} мс";
}
