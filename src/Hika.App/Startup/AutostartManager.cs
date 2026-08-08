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

    // ---- Автозапуск с правами администратора --------------------------------

    private const string TaskName = "HIKA Voice Assistant";

    /// <summary>
    /// Автозапуск для режима с правами администратора.
    ///
    /// Обычная ветка реестра здесь не годится: запущенная из неё программа
    /// с манифестом «asInvoker» правами не обладает, а вариант с повышением
    /// показывал бы запрос UAC при каждом входе в систему — жить с этим
    /// невозможно. Планировщик умеет запускать задачу сразу с нужными
    /// правами и без единого вопроса.
    /// </summary>
    public static bool EnableElevated()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path)) return false;

        if (!Elevation.IsElevated)
        {
            Log.Warn("создание задачи планировщика требует прав администратора", "startup");
            return false;
        }

        // Обычная запись автозапуска убирается, иначе программа стартовала бы дважды.
        Disable();

        var arguments =
            $"/create /tn \"{TaskName}\" /tr \"\\\"{path}\\\" --autostart\" " +
            "/sc onlogon /rl highest /f";

        if (!RunSchtasks(arguments)) return false;

        Log.Info("автозапуск с правами администратора включён", "startup");
        return true;
    }

    public static bool DisableElevated()
    {
        if (!IsElevatedAutostartEnabled()) return true;

        if (!Elevation.IsElevated)
        {
            Log.Warn("удаление задачи планировщика требует прав администратора", "startup");
            return false;
        }

        var ok = RunSchtasks($"/delete /tn \"{TaskName}\" /f");
        if (ok) Log.Info("автозапуск с правами администратора выключен", "startup");

        return ok;
    }

    public static bool IsElevatedAutostartEnabled() => RunSchtasks($"/query /tn \"{TaskName}\"", quiet: true);

    /// <summary>Включён любым из двух способов.</summary>
    public static bool IsAnyEnabled() => IsEnabled() || IsElevatedAutostartEnabled();

    /// <summary>
    /// Приводит автозапуск в соответствие настройкам. Два способа взаимно
    /// исключают друг друга: включённые разом, они запускали бы программу дважды.
    /// </summary>
    public static bool Apply(bool autostart, bool runAsAdmin)
    {
        if (!autostart)
        {
            DisableElevated();
            return Disable();
        }

        if (runAsAdmin)
        {
            if (EnableElevated()) return true;

            // Планировщик не дался (обычно не хватило прав) — пусть будет
            // хотя бы обычный автозапуск, это лучше, чем никакого.
            Log.Warn("не вышло через планировщик, включаю обычный автозапуск", "startup");
            return Enable();
        }

        DisableElevated();
        return Enable();
    }

    private static bool RunSchtasks(string arguments, bool quiet = false)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process is null) return false;

            var error = process.StandardError.ReadToEnd();
            process.StandardOutput.ReadToEnd();
            process.WaitForExit(15000);

            if (process.ExitCode != 0)
            {
                if (!quiet) Log.Warn($"schtasks вернул {process.ExitCode}: {error.Trim()}", "startup");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            if (!quiet) Log.Error("не удалось вызвать планировщик задач", ex, "startup");
            return false;
        }
    }

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
