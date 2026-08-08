using System.Runtime.InteropServices;
using Hika.Diagnostics;

namespace Hika.Catalog;

/// <summary>
/// Читает список установленных приложений из служебной папки Windows shell:AppsFolder.
///
/// Это тот же список, который показывает «Пуск», и он полнее ярлыков: сюда попадают
/// приложения из Microsoft Store, у которых никакого .lnk на диске нет. Запускаются
/// они по идентификатору вида «Microsoft.WindowsCalculator_8wekyb3d8bbwe!App»,
/// который умеет открывать проводник.
/// </summary>
public static class AppsFolderIndexer
{
    /// <summary>
    /// Служебные записи Windows: пользы от них голосом ноль, а мешаться в выдаче
    /// они будут — «Выполнить» похоже по звучанию на слишком многое.
    /// </summary>
    private static readonly string[] Skip =
    {
        "выполнить", "run", "windows powershell ise", "odbc",
        "средство", "компонент", "устаревш", "legacy",
    };

    public static List<CatalogEntry> Index()
    {
        var result = new List<CatalogEntry>();

        // Оболочка Windows — COM с однопоточной моделью. Из чужого потока
        // она работает через маршалинг и иногда просто отказывает,
        // поэтому заводим для перечисления свой STA-поток.
        var thread = new Thread(() => result = IndexCore())
        {
            IsBackground = true,
            Name = "hika-appsfolder",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(TimeSpan.FromSeconds(20)))
        {
            Log.Warn("перечисление установленных приложений затянулось, пропускаю", "catalog");
            return new List<CatalogEntry>();
        }

        return result;
    }

    private static List<CatalogEntry> IndexCore()
    {
        var result = new List<CatalogEntry>();
        object? shell = null;

        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                Log.Warn("Shell.Application недоступен, список приложений Windows пропущен", "catalog");
                return result;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null) return result;

            dynamic dynamicShell = shell;
            dynamic folder = dynamicShell.NameSpace("shell:AppsFolder");
            if (folder is null) return result;

            dynamic items = folder.Items();
            int count = items.Count;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < count; i++)
            {
                try
                {
                    dynamic item = items.Item(i);
                    string name = item.Name;
                    string aumid = item.Path;

                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(aumid)) continue;
                    if (IsSkipped(name)) continue;
                    if (!seen.Add(name)) continue;

                    var names = new List<string> { name };
                    var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length > 1) names.Add(words[^1]);

                    result.Add(CatalogEntry.Create(
                        id: "appsfolder:" + aumid,
                        kind: EntryKind.Installed,
                        // Проводник — штатный способ запустить приложение по такому идентификатору.
                        command: "explorer.exe",
                        args: "shell:AppsFolder\\" + aumid,
                        names: names,
                        weight: 0.95));
                }
                catch
                {
                    // Отдельные элементы папки бывают битыми — пропускаем и идём дальше.
                }
            }

            Log.Info($"установленных приложений найдено: {result.Count}", "catalog");
        }
        catch (Exception ex)
        {
            Log.Warn($"список установленных приложений не прочитался: {ex.Message}", "catalog");
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
            {
                try { Marshal.FinalReleaseComObject(shell); } catch { }
            }
        }

        return result;
    }

    private static bool IsSkipped(string name)
    {
        var lower = name.ToLowerInvariant();
        foreach (var marker in Skip)
        {
            if (lower.Contains(marker)) return true;
        }
        return false;
    }
}
