using Hika.Diagnostics;

namespace Hika.Catalog;

/// <summary>
/// Собирает всё установленное на этом компьютере: приложения Windows
/// и ярлыки из меню «Пуск».
///
/// Живёт отдельно от каталога намеренно. Каталог занимается сопоставлением
/// сказанного с известным и ничего не знает про Windows — благодаря этому
/// его логику можно проверить тестами на любой машине. А обход диска
/// и обращения к оболочке остаются здесь, в части, которая без Windows
/// смысла не имеет.
/// </summary>
public static class InstalledAppsScanner
{
    public static IReadOnlyList<CatalogEntry> Scan()
    {
        var found = new List<CatalogEntry>();

        try { found.AddRange(AppsFolderIndexer.Index()); }
        catch (Exception ex) { Log.Error("список приложений Windows не собрался", ex, "catalog"); }

        try { found.AddRange(StartMenuIndexer.Index()); }
        catch (Exception ex) { Log.Error("меню «Пуск» не прочиталось", ex, "catalog"); }

        // Источники пересекаются: одна и та же программа обычно есть
        // и в списке приложений, и ярлыком.
        return found
            .GroupBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }
}
