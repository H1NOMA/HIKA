using Hika.Diagnostics;
using Microsoft.Win32;

namespace Hika.Startup;

/// <summary>
/// Автозапуск вместе с Windows.
///
/// Используется ветка реестра для текущего пользователя — та же, куда пишут
/// все обычные программы. Ни служб, ни планировщика, ни записей для всей
/// машины: всё это требует прав администратора и выглядит для антивируса
/// куда более настораживающе, чем того стоит запуск программы в трее.
/// </summary>
public static class AutostartManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HIKA";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            Log.Warn($"не удалось прочитать автозапуск: {ex.Message}", "startup");
            return false;
        }
    }

    public static bool Enable()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                Log.Error("путь к исполняемому файлу неизвестен, автозапуск не настроен", "startup");
                return false;
            }

            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return false;

            // Кавычки обязательны: путь почти наверняка содержит пробелы.
            key.SetValue(ValueName, $"\"{path}\" --autostart", RegistryValueKind.String);

            Log.Info($"автозапуск включён: {path}", "startup");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("не удалось включить автозапуск", ex, "startup");
            return false;
        }
    }

    public static bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is not null) key.DeleteValue(ValueName, throwOnMissingValue: false);

            Log.Info("автозапуск выключен", "startup");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("не удалось выключить автозапуск", ex, "startup");
            return false;
        }
    }

    public static bool Set(bool enabled) => enabled ? Enable() : Disable();

    /// <summary>Путь, прописанный в автозапуске, разошёлся с текущим — программу перенесли.</summary>
    public static bool NeedsRepair()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            if (key?.GetValue(ValueName) is not string value) return false;

            var current = Environment.ProcessPath;
            return !string.IsNullOrWhiteSpace(current)
                && !value.Contains(current, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
