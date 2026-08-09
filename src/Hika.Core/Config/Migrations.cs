using Hika.Diagnostics;

namespace Hika.Config;

/// <summary>
/// Подтягивает старый файл настроек к новым умолчаниям.
///
/// Задача выглядит мелкой, но без неё половина работы по ускорению
/// не доходит до человека. Файл настроек создаётся при первом запуске
/// со всеми значениями, выписанными явно. Дальше поменять значение
/// по умолчанию в коде бессмысленно: у человека в файле лежит старое,
/// и он останется со старым навсегда — с тем же ожиданием и той же широкой
/// каймой, — искренне полагая, что обновление ничего не изменило.
///
/// Правило одно, и оно определяет всё остальное: меняется только то,
/// что в точности совпадает со старым умолчанием. Совпало — значит,
/// человек этого не трогал, и подставить новое честно. Отличается —
/// значит, выбрал сам, и трогать нельзя ни при каких обстоятельствах.
/// </summary>
public static class Migrations
{
    /// <summary>Применяет всё, что нужно. Возвращает true, если файл стоит переписать.</summary>
    public static bool Apply(HikaConfig config)
    {
        if (config.Version >= HikaConfig.CurrentVersion) return false;

        var from = config.Version;
        var changes = new List<string>();

        if (config.Version < 2) ToVersion2(config, changes);
        if (config.Version < 3) ToVersion3(config, changes);
        if (config.Version < 4) ToVersion4(config, changes);

        config.Version = HikaConfig.CurrentVersion;

        Log.Info(changes.Count == 0
            ? $"настройки обновлены до версии {HikaConfig.CurrentVersion} (изменять было нечего)"
            : $"настройки обновлены с версии {from}: {string.Join(", ", changes)}", "config");

        return true;
    }

    /// <summary>
    /// Версия 2 — отзывчивость и ширина каймы.
    ///
    /// Все три значения оказались осторожнее, чем нужно, и все три человек
    /// чувствует напрямую: два первых — временем ожидания перед каждой
    /// командой, третье — полосой света шириной в треть экрана.
    /// </summary>
    private static void ToVersion2(HikaConfig c, List<string> changes)
    {
        // Полсекунды тишины перед началом распознавания — чистое ожидание.
        if (c.Audio.SilenceMs == 500) Change(changes, "конец фразы 500 -> 400 мс", () => c.Audio.SilenceMs = 400);

        // Ранняя проверка имени запускалась почти вдвое позже нужного,
        // и ровно на столько же позже загоралась кайма.
        if (c.Speech.ProbeAfterMs == 900) Change(changes, "проверка имени 900 -> 600 мс", () => c.Speech.ProbeAfterMs = 600);

        // Кайма была шире, чем требуется, чтобы обозначить себя.
        if (Same(c.Overlay.Thickness, 0.09)) Change(changes, "толщина каймы 0.09 -> 0.07", () => c.Overlay.Thickness = 0.07);
    }

    /// <summary>
    /// Версия 3 — поиск в интернете только по просьбе.
    ///
    /// Прежнее поведение отправляло в поисковик всё, что не нашлось
    /// в каталоге. Выглядело это так: человек говорит что-то рядом
    /// с компьютером, а браузер открывает его же слова — и повторяется
    /// это столько раз, сколько он говорит.
    /// </summary>
    private static void ToVersion3(HikaConfig c, List<string> changes)
    {
        if (c.Behavior.WebSearchFallback)
            Change(changes, "поиск в интернете — только по просьбе", () => c.Behavior.WebSearchFallback = false);
    }

    /// <summary>
    /// Версия 4 — мгновенная реакция на имя.
    ///
    /// Проверка имени стала скользящей и подешевела настолько, что ждать
    /// перед первой попыткой больше незачем. Прежние шестьсот миллисекунд —
    /// это ровно та задержка, которую человек видит глазами между «Хико»
    /// и загоревшейся каймой.
    /// </summary>
    private static void ToVersion4(HikaConfig c, List<string> changes)
    {
        if (c.Speech.ProbeAfterMs is 600 or 900)
            Change(changes, $"проверка имени {c.Speech.ProbeAfterMs} -> 350 мс", () => c.Speech.ProbeAfterMs = 350);
    }

    private static void Change(List<string> changes, string description, Action apply)
    {
        apply();
        changes.Add(description);
    }

    /// <summary>Сравнение дробных с допуском: 0.09 из файла не обязано быть ровно 0.09.</summary>
    private static bool Same(double a, double b) => Math.Abs(a - b) < 1e-9;
}
