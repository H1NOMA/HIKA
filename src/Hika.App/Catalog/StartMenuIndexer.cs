using Hika.Diagnostics;

namespace Hika.Catalog;

/// <summary>
/// Собирает ярлыки из меню «Пуск».
///
/// Благодаря этому HIKA открывает и то, чего нет во встроенном каталоге:
/// всё, что человек установил сам, у него в «Пуске» есть.
/// Сами ярлыки запускаются через ShellExecute — разбирать .lnk не нужно.
/// </summary>
public static class StartMenuIndexer
{
    /// <summary>Служебные ярлыки, которые не нужно предлагать голосом — и особенно не нужно случайно запускать.</summary>
    private static readonly string[] NoiseMarkers =
    {
        "uninstall", "удалить", "деинсталл", "remove ",
        "readme", "read me", "прочти", "лицензи", "license", "licence",
        "help", "справка", "documentation", "документаци", "руководство",
        "website", "веб-сайт", "домашняя страница", "homepage",
        "changelog", "release notes", "что нового",
        "repair", "восстановление", "modify",
    };

    public static List<CatalogEntry> Index()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
        };

        var result = new List<CatalogEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;

            try
            {
                foreach (var file in EnumerateShortcuts(root))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (IsNoise(name) || IsNoise(Path.GetDirectoryName(file) ?? "")) continue;
                    if (!seen.Add(name)) continue;

                    // Названия вида «Google Chrome» человек произносит и целиком,
                    // и одним словом — добавляем оба варианта.
                    var names = new List<string> { name };
                    var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length > 1) names.Add(words[^1]);

                    result.Add(CatalogEntry.Create(
                        id: "startmenu:" + name,
                        kind: EntryKind.Installed,
                        command: file,
                        names: names,
                        // Ярлыки слабее встроенного каталога: он выверен вручную,
                        // а тут попадается всякое.
                        weight: 0.93));
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"меню «Пуск» частично не прочиталось ({root}): {ex.Message}", "catalog");
            }
        }

        Log.Info($"из меню «Пуск» собрано записей: {result.Count}", "catalog");
        return result;
    }

    private static IEnumerable<string> EnumerateShortcuts(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MaxRecursionDepth = 6,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };

        IEnumerable<string> links;
        try { links = Directory.EnumerateFiles(root, "*.lnk", options); }
        catch { yield break; }

        foreach (var f in links) yield return f;

        IEnumerable<string> urls;
        try { urls = Directory.EnumerateFiles(root, "*.url", options); }
        catch { yield break; }

        foreach (var f in urls) yield return f;
    }

    private static bool IsNoise(string text)
    {
        var lower = text.ToLowerInvariant();
        foreach (var marker in NoiseMarkers)
        {
            if (lower.Contains(marker)) return true;
        }
        return false;
    }
}
