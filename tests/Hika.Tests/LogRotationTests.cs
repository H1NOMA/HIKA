using Hika.Diagnostics;
using Xunit;

namespace Hika.Tests;

/// <summary>
/// Когда журналу пора начинать новый файл.
///
/// Программа живёт в трее и не выключается неделями, а имя файла до сих пор
/// выбиралось один раз, при запуске. То есть месяц работы — один файл, и
/// уборка старых журналов, которая тоже делается при запуске, удалить его
/// не может: он текущий. На уровне «отладка» — а его включают именно тогда,
/// когда что-то не работает, — туда идёт строка на каждую проверку имени.
/// </summary>
public class LogRotationTests
{
    private static readonly DateTime День = new(2026, 8, 28, 14, 30, 0);

    [Fact]
    public void ПокаТотЖеДеньИФайлНеРаспух_НичегоНеМеняем()
        => Assert.False(LogRotation.ShouldRoll(День, День.AddHours(3), 1024));

    [Fact]
    public void НаступилСледующийДень_НовыйФайл()
        => Assert.True(LogRotation.ShouldRoll(День, День.AddDays(1), 10));

    [Fact]
    public void ФайлДоросДоПредела_НовыйФайл()
        => Assert.True(LogRotation.ShouldRoll(День, День.AddMinutes(1), LogRotation.MaxBytes));

    /// <summary>
    /// Полночь — это смена дня, а не «прошло 24 часа»: программа, запущенная
    /// в 23:59, обязана начать новый файл через минуту, а не через сутки.
    /// </summary>
    [Fact]
    public void ПолночьЭтоСменаДня()
    {
        var поздно = new DateTime(2026, 8, 28, 23, 59, 0);
        Assert.True(LogRotation.ShouldRoll(поздно, поздно.AddMinutes(2), 10));
    }

    [Fact]
    public void ПерваяЧастьДняБезНомера()
        => Assert.Equal("hika-2026-08-28.log", LogRotation.FileName(День, 1));

    [Fact]
    public void ВтораяЧастьСНомером()
        => Assert.Equal("hika-2026-08-28-2.log", LogRotation.FileName(День, 2));

    [Fact]
    public void НетФайлов_НачинаемСПервой()
        => Assert.Equal(1, LogRotation.LastPart(Array.Empty<string>(), День));

    /// <summary>
    /// Перезапуск посреди дня продолжает последнюю часть, а не заводит новую:
    /// иначе за день их набралось бы столько, сколько раз человек
    /// перезагрузил компьютер.
    /// </summary>
    [Fact]
    public void ПродолжаемПоследнююЧасть()
    {
        var файлы = new[]
        {
            Path.Combine("logs", "hika-2026-08-28.log"),
            Path.Combine("logs", "hika-2026-08-28-2.log"),
            Path.Combine("logs", "hika-2026-08-28-3.log"),
        };

        Assert.Equal(3, LogRotation.LastPart(файлы, День));
    }

    [Fact]
    public void ЧужиеДниНеСчитаются()
    {
        var файлы = new[]
        {
            Path.Combine("logs", "hika-2026-08-27-9.log"),
            Path.Combine("logs", "hika-2026-08-28.log"),
        };

        Assert.Equal(1, LogRotation.LastPart(файлы, День));
    }

    [Fact]
    public void МусорВИмениНеЛомаетПодсчёт()
    {
        var файлы = new[]
        {
            Path.Combine("logs", "hika-2026-08-28-абв.log"),
            Path.Combine("logs", "hika-2026-08-28-.log"),
            Path.Combine("logs", "hika-2026-08-28-2.log"),
        };

        Assert.Equal(2, LogRotation.LastPart(файлы, День));
    }
}
