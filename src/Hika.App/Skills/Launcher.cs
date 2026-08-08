using System.Diagnostics;
using Hika.Catalog;
using Hika.Diagnostics;

namespace Hika.Skills;

public sealed record SkillResult(bool Success, string Description)
{
    /// <summary>
    /// Что именно запустили — идентификатор записи каталога.
    ///
    /// Нужно обучению: чтобы связать неудачную формулировку с удавшейся,
    /// надо знать, чем именно кончилась вторая. Пусто для всего, что
    /// каталога не касается — громкости, скриншотов, поиска.
    /// </summary>
    public string EntryId { get; init; } = "";

    public static SkillResult Ok(string what) => new(true, what);
    public static SkillResult Fail(string why) => new(false, why);

    public SkillResult From(CatalogEntry entry) => this with { EntryId = entry.Id };
}

/// <summary>
/// Запуск программ и открытие сайтов.
///
/// Порядок попыток выстроен от самого надёжного к самому общему. Отдельно стоит
/// отметить запуск по короткому имени («chrome», «winword»): Windows держит
/// реестр путей к приложениям, и большинство известных программ открывается
/// именно так — без единого пути в каталоге и независимо от того, куда человек
/// их установил.
/// </summary>
public static class Launcher
{
    public static SkillResult Launch(CatalogEntry entry)
    {
        var what = entry.DisplayName;

        // Сайты и схемы вида ms-settings: открывает сама оболочка.
        if (entry.Kind == EntryKind.Site || LooksLikeUri(entry.Command))
            return StartShell(entry.Command, entry.Args, what);

        // Полный путь — если файл на месте, это самый надёжный вариант.
        var expanded = Environment.ExpandEnvironmentVariables(entry.Command);
        if (IsExistingFile(expanded))
            return StartShell(expanded, entry.Args, what, Path.GetDirectoryName(expanded));

        // Запасные пути из каталога.
        foreach (var candidate in entry.Paths)
        {
            var path = Environment.ExpandEnvironmentVariables(candidate);
            if (IsExistingFile(path))
                return StartShell(path, entry.Args, what, Path.GetDirectoryName(path));
        }

        // Короткое имя — Windows поищет его сама в реестре путей приложений.
        var byName = StartShell(entry.Command, entry.Args, what);
        if (byName.Success) return byName;

        Log.Warn($"не удалось запустить «{what}»: команда «{entry.Command}» не сработала, запасные пути не найдены", "launch");
        return SkillResult.Fail($"«{what}» не найдено на этом компьютере");
    }

    /// <summary>Открыть произвольную ссылку.</summary>
    public static SkillResult OpenUrl(string url, string description)
    {
        if (!url.Contains("://") && !url.Contains(':')) url = "https://" + url;
        return StartShell(url, "", description);
    }

    private static SkillResult StartShell(string command, string args, string what, string? workingDirectory = null)
    {
        // Если HIKA работает с правами администратора, запущенные ею программы
        // унаследовали бы эти права — а этого нельзя допускать. Chrome в таком
        // виде вообще откажется стартовать, остальные начнут вести себя странно.
        // Отдаём запуск проводнику, и программа достаётся обычному пользователю.
        //
        // Аргументы командной строки этот способ не переносит, поэтому при
        // их наличии идём обычным путём — с ключами запускаются в основном
        // служебные вещи, где повышенные права как раз уместны.
        if (Startup.Elevation.IsElevated && string.IsNullOrWhiteSpace(args))
        {
            if (Startup.Elevation.TryLaunchAsUser(command, workingDirectory))
            {
                Log.Info($"открыто: {what}", "launch");
                return SkillResult.Ok(what);
            }

            // Проводник не справился — падаем на обычный запуск.
        }

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = command,
                // Обязательно: без этого не откроются ни ссылки, ни ярлыки,
                // ни схемы вроде ms-settings:, ни поиск по реестру путей.
                UseShellExecute = true,
            };

            if (!string.IsNullOrWhiteSpace(args)) info.Arguments = args;

            if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
                info.WorkingDirectory = workingDirectory;

            using var process = Process.Start(info);

            Log.Info($"открыто: {what}", "launch");
            return SkillResult.Ok(what);
        }
        catch (Exception ex)
        {
            Log.Debug($"«{command}» не запустилось: {ex.Message}", "launch");
            return SkillResult.Fail(ex.Message);
        }
    }

    private static bool LooksLikeUri(string command)
    {
        if (command.Contains("://")) return true;

        // Схемы вида ms-settings:, ms-windows-store:, microsoft.windows.camera:
        var colon = command.IndexOf(':');
        if (colon <= 1) return false;                       // «C:\...» — это путь, а не схема
        if (command.Contains('\\') || command.Contains('/')) return false;

        return true;
    }

    private static bool IsExistingFile(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path)
                && (path.Contains('\\') || path.Contains('/'))
                && File.Exists(path);
        }
        catch { return false; }
    }
}
