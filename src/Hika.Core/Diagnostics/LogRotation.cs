namespace Hika.Diagnostics;

/// <summary>
/// Решение о том, в какой файл писать журнал дальше.
///
/// Вынесено отдельно ради одного: это единственная часть журнала, которую
/// можно проверить тестами. Сама запись на диск непроверяема, а вот
/// «когда пора начать новый файл» — обычная арифметика, и ошибка в ней
/// стоит дорого.
///
/// Стоила она следующего. Имя файла подставлялось один раз при запуске,
/// и всё. Программа, которая живёт в трее и не выключается неделями, писала
/// в файл с датой того дня, когда её запустили, — то есть в один файл
/// за месяц. Уборка старых журналов при этом тоже выполнялась ровно раз,
/// при запуске, так что удалить этот файл было некому. На уровне «отладка»,
/// который человек включает как раз тогда, когда что-то не работает, туда
/// уходит по строке на каждую проверку имени — сотни мегабайт за сутки.
///
/// Отсюда два повода начать новый файл: наступил следующий день или текущий
/// перерос предел.
/// </summary>
public static class LogRotation
{
    /// <summary>Предел одного файла. Дальше начинается следующая часть.</summary>
    public const long MaxBytes = 8L * 1024 * 1024;

    /// <summary>
    /// Пора ли начинать новый файл.
    /// </summary>
    /// <param name="fileDay">День, которым помечен текущий файл.</param>
    /// <param name="now">Сейчас.</param>
    /// <param name="written">Сколько уже в текущем файле.</param>
    public static bool ShouldRoll(DateTime fileDay, DateTime now, long written)
        => now.Date != fileDay.Date || written >= MaxBytes;

    /// <summary>
    /// Имя файла журнала. Первая часть дня — без номера: у большинства
    /// людей файл за день так и останется единственным, и «hika-2026-08-28.log»
    /// понятнее, чем «hika-2026-08-28-1.log».
    /// </summary>
    public static string FileName(DateTime day, int part)
        => part <= 1
            ? $"hika-{day:yyyy-MM-dd}.log"
            : $"hika-{day:yyyy-MM-dd}-{part}.log";

    /// <summary>
    /// С какой части продолжать, если программу перезапустили посреди дня.
    ///
    /// Берётся последняя существующая часть, а не следующая за ней: пока
    /// файл не дорос до предела, дописывать надо в него. Иначе каждый
    /// перезапуск плодил бы новый файл, и за день их набиралось бы столько,
    /// сколько раз человек перезагрузил компьютер.
    /// </summary>
    public static int LastPart(IEnumerable<string> existingNames, DateTime day)
    {
        var prefix = $"hika-{day:yyyy-MM-dd}";
        var best = 0;

        foreach (var path in existingNames)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            var tail = name[prefix.Length..];

            if (tail.Length == 0) { best = Math.Max(best, 1); continue; }
            if (tail[0] != '-') continue;
            if (int.TryParse(tail[1..], out var part) && part > 0) best = Math.Max(best, part);
        }

        return Math.Max(1, best);
    }
}
