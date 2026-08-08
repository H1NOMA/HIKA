using System.Runtime.InteropServices;
using System.Windows.Forms;
using Hika.Audio;
using Hika.Config;
using Hika.Diagnostics;
using Hika.Startup;
using Hika.Tray;

namespace Hika;

internal static class Program
{
    private const string InstanceMutexName = "Global\\HIKA_VoiceAssistant_SingleInstance";

    [STAThread]
    private static int Main(string[] args)
    {
        AppPaths.EnsureCreated();

        var isCommandLine = args.Any(a =>
            a is "--diagnose" or "-d" or "--list-audio" or "--help" or "-h" or "--version"
                 or "--enable-autostart" or "--disable-autostart");

        if (isCommandLine) AttachToParentConsole();

        var store = new ConfigStore();
        var config = store.Load();

        Log.MinimumLevel = ParseLevel(config.Behavior.LogLevel);
        Log.Initialize(AppPaths.LogDirectory, consoleEcho: isCommandLine);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Log.Error("необработанное исключение", ex, "fatal");
            Log.Flush(TimeSpan.FromSeconds(2));
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("необработанная ошибка в фоновой задаче", e.Exception, "fatal");
            e.SetObserved();
        };

        try
        {
            return Dispatch(args, store, config);
        }
        catch (Exception ex)
        {
            Log.Error("запуск сорвался", ex, "fatal");

            if (isCommandLine) Console.Error.WriteLine($"Ошибка: {ex.Message}");
            else MessageBox.Show($"HIKA не запустилась.\n\n{ex.Message}\n\nПодробности: {AppPaths.LogDirectory}",
                "HIKA", MessageBoxButtons.OK, MessageBoxIcon.Error);

            return 1;
        }
        finally
        {
            Log.Flush(TimeSpan.FromSeconds(2));
        }
    }

    private static int Dispatch(string[] args, ConfigStore store, HikaConfig config)
    {
        if (args.Contains("--help") || args.Contains("-h")) return PrintHelp();
        if (args.Contains("--version")) { Console.WriteLine("HIKA 0.1.0"); return 0; }

        if (args.Contains("--list-audio"))
        {
            Console.WriteLine("Микрофоны в системе:");
            foreach (var device in MicrophoneCapture.ListDevices())
                Console.WriteLine($"  {(device.IsDefault ? "*" : " ")} {device.Name}");
            Console.WriteLine();
            Console.WriteLine("Звёздочкой отмечен микрофон по умолчанию.");
            Console.WriteLine("Чтобы выбрать другой, впишите часть его имени в audio.device в config.json.");
            return 0;
        }

        if (args.Contains("--enable-autostart"))
        {
            var ok = AutostartManager.Enable();
            Console.WriteLine(ok ? "Автозапуск включён." : "Не вышло. Подробности в журнале.");
            return ok ? 0 : 1;
        }

        if (args.Contains("--disable-autostart"))
        {
            var ok = AutostartManager.Disable();
            Console.WriteLine(ok ? "Автозапуск выключен." : "Не вышло. Подробности в журнале.");
            return ok ? 0 : 1;
        }

        if (args.Contains("--diagnose") || args.Contains("-d"))
            return SelfTest.RunAsync(args).GetAwaiter().GetResult();

        return RunTray(store, config, launchedByWindows: args.Contains("--autostart"));
    }

    private static int RunTray(ConfigStore store, HikaConfig config, bool launchedByWindows)
    {
        // Второй экземпляр — это второй открытый микрофон и две реакции на каждую
        // команду. Проверку делаем до всего остального.
        using var mutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirst);

        if (!isFirst)
        {
            Log.Warn("HIKA уже запущена, второй экземпляр закрывается", "startup");

            if (!launchedByWindows)
                MessageBox.Show("HIKA уже работает — значок в трее, рядом с часами.",
                    "HIKA", MessageBoxButtons.OK, MessageBoxIcon.Information);

            return 0;
        }

        ApplicationConfiguration.Initialize();

        using var host = new AppHost(store);
        using var tray = new TrayIcon();

        tray.MuteToggleRequested += () =>
        {
            host.ToggleMute();
            tray.UpdateState(host.State, host.Muted);
        };

        tray.DiagnosticsRequested += () => LaunchDiagnostics(tray);
        tray.ExitRequested += () => Application.Exit();

        host.StateChanged += state =>
        {
            try { tray.UpdateState(state, host.Muted); }
            catch { /* значок мог уже исчезнуть */ }
        };

        host.StartupProblem += message =>
        {
            Log.Warn($"проблема при запуске: {message}", "startup");
            tray.ShowMessage("HIKA", message, ToolTipIcon.Warning);
        };

        store.Changed += updated =>
        {
            Log.MinimumLevel = ParseLevel(updated.Behavior.LogLevel);
            host.ApplyConfig(updated);
        };

        store.StartWatching();

        // Программу могли перенести в другую папку — тогда запись автозапуска
        // указывает в пустоту, и Windows молча ничего не запустит.
        if (AutostartManager.NeedsRepair())
        {
            Log.Info("путь в автозапуске устарел, обновляю", "startup");
            AutostartManager.Enable();
        }

        if (config.Behavior.Autostart && !AutostartManager.IsEnabled()) AutostartManager.Enable();

        var started = host.StartAsync().GetAwaiter().GetResult();
        tray.UpdateState(host.State, host.Muted);

        if (started && !launchedByWindows)
        {
            tray.ShowMessage("HIKA работает",
                $"Скажите «{string.Join("» или «", config.Wake.Words.Select(Capitalize))}» и команду. " +
                "Первый запуск может занять несколько минут — скачиваются модели.");
        }

        Application.Run();
        return 0;
    }

    private static void LaunchDiagnostics(TrayIcon tray)
    {
        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path)) return;

            // Диагностика открывается в отдельном окне консоли: ей нужен микрофон,
            // а он уже занят основным экземпляром — поэтому запускаем как есть
            // и предупреждаем человека.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k \"\"{path}\" --diagnose\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Error("не удалось запустить диагностику", ex, "tray");
            tray.ShowMessage("HIKA", "Диагностика не запустилась. Подробности в журнале.", ToolTipIcon.Warning);
        }
    }

    private static string Capitalize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static int PrintHelp()
    {
        Console.WriteLine("""
            HIKA — голосовое управление Windows.

            Запуск без аргументов: программа уходит в трей и слушает микрофон.

              --diagnose, -d        проверить всю цепочку и сохранить отчёт
              --seconds N           сколько секунд записывать при проверке (по умолчанию 6)
              --list-audio          показать доступные микрофоны
              --enable-autostart    запускать вместе с Windows
              --disable-autostart   не запускать вместе с Windows
              --version             версия
              --help, -h            эта справка

            Настройки: %APPDATA%\HIKA\config.json
            Журнал   : %APPDATA%\HIKA\logs
            """);

        return 0;
    }

    private static LogLevel ParseLevel(string? value) => (value ?? "info").Trim().ToLowerInvariant() switch
    {
        "trace" => LogLevel.Trace,
        "debug" => LogLevel.Debug,
        "warn" or "warning" => LogLevel.Warn,
        "error" => LogLevel.Error,
        _ => LogLevel.Info,
    };

    /// <summary>
    /// Программа собрана как оконная, поэтому своей консоли у неё нет.
    /// Для запусков с аргументами подключаемся к консоли, из которой её позвали,
    /// иначе весь вывод уйдёт в никуда.
    /// </summary>
    private static void AttachToParentConsole()
    {
        try
        {
            if (!AttachConsole(-1)) AllocConsole();

            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch
        {
            // Без консоли останется только журнал — не смертельно.
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();
}
