using System.Runtime.InteropServices;
using System.Windows.Forms;
using Hika.Audio;
using Hika.Config;
using Hika.Diagnostics;
using Hika.Startup;
using Hika.Tray;
using Hika.Ui;

namespace Hika;

internal static class Program
{
    private const string InstanceMutexName = "Global\\HIKA_VoiceAssistant_SingleInstance";

    [STAThread]
    private static int Main(string[] args)
    {
        AppPaths.EnsureCreated();

        var isCommandLine = args.Any(a =>
            a is "--diagnose" or "-d" or "--listen" or "-l" or "--list-audio"
                 or "--help" or "-h" or "--version"
                 or "--enable-autostart" or "--disable-autostart" or "--test-overlay");

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
        if (args.Contains("--version")) { Console.WriteLine(BuildInfo.Describe()); return 0; }

        if (args.Contains("--list-audio"))
        {
            Console.WriteLine(BuildInfo.Describe());
            Console.WriteLine();
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

        if (args.Contains("--listen") || args.Contains("-l"))
            return LiveListen.RunAsync(args).GetAwaiter().GetResult();

        if (args.Contains("--test-overlay"))
            return TestOverlay(config);

        return RunTray(store, config, args, launchedByWindows: args.Contains("--autostart"));
    }

    private static int RunTray(ConfigStore store, HikaConfig config, string[] args, bool launchedByWindows)
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

        // Повышение прав, если оно включено и мы ещё не повышены.
        // Флаг перезапуска обязателен: без него отказ в UAC уводил бы
        // программу в бесконечный круг запросов.
        if (config.Behavior.RunAsAdmin && !Elevation.IsElevated && !args.Contains(Elevation.RelaunchFlag))
        {
            if (Elevation.RelaunchElevated(args)) return 0;
            Log.Info("работаю с обычными правами", "startup");
        }

        ApplicationConfiguration.Initialize();
        Ui.Theme.ApplyPersona(config.Persona);

        if (Elevation.IsElevated) Log.Info("права администратора: есть", "startup");

        using var host = new AppHost(store);
        using var tray = new TrayIcon(config.Persona);

        // Окно создаётся один раз и дальше только прячется: пересоздавать его
        // на каждое открытие — терять и позицию, и выбранный раздел.
        SettingsWindow? settings = null;

        SettingsWindow GetSettings()
        {
            if (settings is null || settings.IsDisposed)
            {
                settings = new SettingsWindow(
                    store,
                    levelSource: () => host.CurrentLevel,
                    statusSource: () => (
                        DescribeState(host.State, host.Muted),
                        host.MicrophoneName,
                        host.RecognizerDescription,
                        host.CatalogSize),
                    onApply: updated =>
                    {
                        host.ApplyConfig(updated);
                        tray.SetPersona(updated.Persona);
                        tray.UpdateState(host.State, host.Muted);
                    },
                    onLiveListen: () => LaunchInConsole(tray, "--listen"),
                    onDiagnostics: () => LaunchInConsole(tray, "--diagnose"),
                    onTestOverlay: () => host.TestOverlay());
            }

            return settings;
        }

        tray.SettingsRequested += () =>
        {
            try { GetSettings().ShowWindow(); }
            catch (Exception ex)
            {
                // Не всплывающая подсказка, а полноценное окно с текстом ошибки.
                // Подсказку легко пропустить, и тогда «настройки не открываются»
                // остаётся без единой зацепки — а зацепка вот она.
                Log.Error("окно настроек не открылось", ex, "ui");

                MessageBox.Show(
                    $"Настройки не открылись.\n\n{ex.GetType().Name}: {ex.Message}\n\n" +
                    $"Подробности: {AppPaths.LogDirectory}",
                    "HIKA", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        tray.MuteToggleRequested += () =>
        {
            host.ToggleMute();
            tray.UpdateState(host.State, host.Muted);
        };

        tray.DiagnosticsRequested += () => LaunchInConsole(tray, "--diagnose");
        tray.LiveListenRequested += () => LaunchInConsole(tray, "--listen");
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

        if (config.Behavior.Autostart && !AutostartManager.IsAnyEnabled())
            AutostartManager.Apply(true, config.Behavior.RunAsAdmin);

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

    /// <summary>Показывает свечение без микрофона — отдельным экземпляром, из командной строки.</summary>
    private static int TestOverlay(HikaConfig config)
    {
        Console.WriteLine(BuildInfo.Describe());
        Console.WriteLine();
        Console.WriteLine("Проверка свечения. Микрофон не используется.");
        Console.WriteLine("Кайма должна пройти по кругу: слабое свечение, затем яркое");
        Console.WriteLine("с пульсацией, затем бегущая волна и зелёная вспышка.");
        Console.WriteLine();

        using var overlay = new Overlay.GlowOverlay();
        overlay.Start(config.Overlay, config.Persona);

        // Окна создаются в своём потоке — дадим ему секунду.
        Thread.Sleep(1000);

        Console.WriteLine($"Окон свечения создано: {overlay.BandCount}");

        if (overlay.BandCount == 0)
        {
            Console.WriteLine();
            Console.WriteLine("НИ ОДНОГО ОКНА НЕ СОЗДАНО — показывать нечего.");
            Console.WriteLine("Проверьте, что свечение включено в настройках. Подробности в журнале:");
            Console.WriteLine("  " + AppPaths.LogDirectory);
            return 1;
        }

        Console.WriteLine("Смотрите на экран…");
        overlay.RunSelfTestAsync().GetAwaiter().GetResult();

        Console.WriteLine();
        Console.WriteLine("Состояние отрисовки: " + overlay.DescribeHealth());
        Console.WriteLine();
        Console.WriteLine("Если по краям ничего не появилось, а строка выше говорит «в порядке»,");
        Console.WriteLine("значит окна рисуются, но их что-то перекрывает. Пришлите эту строку");
        Console.WriteLine("и журнал: " + AppPaths.LogDirectory);

        return 0;
    }

    /// <summary>Открывает второй экземпляр в отдельном окне консоли — там живут проверки.</summary>
    private static void LaunchInConsole(TrayIcon tray, string argument)
    {
        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path)) return;

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                // /k, а не /c: окно остаётся открытым, и человек успевает
                // прочитать вывод, а не смотрит, как оно закрылось.
                Arguments = $"/k \"\"{path}\" {argument}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Error($"не удалось запустить {argument}", ex, "tray");
            tray.ShowMessage("HIKA", "Проверка не запустилась. Подробности в журнале.", ToolTipIcon.Warning);
        }
    }

    private static string DescribeState(HostState state, bool muted) => muted
        ? "микрофон выключен"
        : state switch
        {
            HostState.Starting => "запускается",
            HostState.Idle => "слушает",
            HostState.Sensing => "слышу голос",
            HostState.Armed => "жду команду",
            HostState.Working => "выполняю",
            HostState.Failed => "ошибка, смотрите журнал",
            _ => "—",
        };

    private static string Capitalize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static int PrintHelp()
    {
        Console.WriteLine("""
            HIKA — голосовое управление Windows.

            Запуск без аргументов: программа уходит в трей и слушает микрофон.

              --listen, -l          живая проверка: показывать всё услышанное
                                    и что с ним стало. Лучший способ понять,
                                    почему команда не сработала
              --execute             вместе с --listen: ещё и выполнять команды
              --test-overlay        показать свечение, не трогая микрофон
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
