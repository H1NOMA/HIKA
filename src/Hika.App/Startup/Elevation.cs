using System.Diagnostics;
using System.Security.Principal;
using Hika.Diagnostics;

namespace Hika.Startup;

/// <summary>
/// Работа с правами администратора.
///
/// Права здесь принципиально необязательные и по умолчанию выключены.
/// Ассистенту с постоянно открытым микрофоном они не нужны почти никогда,
/// а вреда от них хватает: антивирусы относятся к такой программе заметно
/// строже, перетаскивание файлов из проводника в неё перестаёт работать,
/// и — самое неприятное — запущенные ею программы наследуют повышенные
/// права. Chrome в таком виде просто отказывается стартовать, а остальные
/// начинают вести себя странно.
///
/// Единственная настоящая польза: только с правами администратора можно
/// управлять окнами программ, которые сами запущены от администратора.
/// Ради этого случая режим и существует.
///
/// Наследование прав вылечено: когда HIKA работает с повышенными правами,
/// она запускает программы через проводник, и они достаются обычному
/// пользователю, как если бы он открыл их сам.
/// </summary>
public static class Elevation
{
    /// <summary>Флаг перезапуска — защита от бесконечного цикла повышения прав.</summary>
    public const string RelaunchFlag = "--elevated-relaunch";

    private static bool? _cached;

    public static bool IsElevated
    {
        get
        {
            if (_cached.HasValue) return _cached.Value;

            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                _cached = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex)
            {
                Log.Warn($"не удалось определить права: {ex.Message}", "startup");
                _cached = false;
            }

            return _cached.Value;
        }
    }

    /// <summary>
    /// Перезапускает программу с повышенными правами.
    /// Возвращает true, если новый экземпляр запущен и текущему пора уходить.
    /// </summary>
    public static bool RelaunchElevated(IEnumerable<string> originalArgs)
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            Log.Error("путь к исполняемому файлу неизвестен, повышение прав невозможно", "startup");
            return false;
        }

        var arguments = originalArgs
            .Where(a => a != RelaunchFlag)
            .Append(RelaunchFlag)
            .Select(a => a.Contains(' ') ? $"\"{a}\"" : a);

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = path,
                Arguments = string.Join(' ', arguments),
                UseShellExecute = true,
                // Именно этот глагол показывает запрос UAC.
                Verb = "runas",
            };

            Process.Start(info);
            Log.Info("перезапуск с правами администратора", "startup");
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // 1223 — человек нажал «Нет» в запросе UAC. Это его право,
            // и продолжать работу без повышенных прав совершенно нормально.
            Log.Info("в повышении прав отказано, продолжаю с обычными", "startup");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("не удалось перезапуститься с правами администратора", ex, "startup");
            return false;
        }
    }

    /// <summary>
    /// Запускает программу от имени обычного пользователя, даже если сама HIKA
    /// работает с повышенными правами.
    ///
    /// Приём известный: просим проводник открыть цель. Проводник работает
    /// с обычными правами, и запущенное им их и получает — ровно как если бы
    /// человек щёлкнул по значку сам.
    /// </summary>
    public static bool TryLaunchAsUser(string target, string? workingDirectory = null)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{target}\"",
                UseShellExecute = true,
            };

            if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
                info.WorkingDirectory = workingDirectory;

            using var process = Process.Start(info);

            Log.Debug($"запуск через проводник (без наследования прав): {target}", "launch");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"запуск через проводник не удался: {ex.Message}", "launch");
            return false;
        }
    }
}
