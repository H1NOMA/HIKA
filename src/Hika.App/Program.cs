using System.Runtime.InteropServices;
using System.Windows.Forms;
using Hika.Audio;
using Hika.Config;
using Hika.Diagnostics;
using Hika.Speech;
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
                 or "--help" or "-h" or "--version" or "--list-voices" or "--say"
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

    /// <summary>
    /// Показывает, чем программа может говорить.
    ///
    /// Нужно потому, что «голос звучит механически» и «голос вообще не звучит» —
    /// разные беды с разными решениями, а отличить их без списка нельзя.
    /// Нейроголоса отмечены отдельно: если их в списке нет, дальше говорить
    /// не о чем, пока человек их не поставит.
    /// </summary>
    private static int ListVoices(HikaConfig config)
    {
        Console.WriteLine(BuildInfo.Describe());
        Console.WriteLine();

        using var voice = new VoiceService();
        voice.StartAsync(config.Voice, config.Speech.Language).GetAwaiter().GetResult();

        var voices = voice.ListAllVoicesAsync().GetAwaiter().GetResult();

        if (voices.Count == 0)
        {
            Console.WriteLine("Голосов не нашлось вовсе. Такое бывает на урезанных сборках Windows —");
            Console.WriteLine("тогда остаются только нейроголоса Microsoft через интернет: включите их");
            Console.WriteLine("в настройках, раздел «Голос».");
            return 1;
        }

        Console.WriteLine("Голоса, доступные для озвучки:");
        foreach (var item in voices)
            Console.WriteLine($"  {(item.IsNeural ? "★" : " ")} {item.Name}  [{item.Language}]");

        Console.WriteLine();
        Console.WriteLine($"Сейчас выбран: {voice.Description}");

        if (!voices.Any(v => v.IsNeural))
        {
            Console.WriteLine();
            Console.WriteLine("Нейроголосов (★) в системе нет — поэтому звук режет ухо.");
            Console.WriteLine("Ставятся один раз: Параметры → Время и язык → Речь →");
            Console.WriteLine("Управление голосами → Добавить голоса → русский.");
            Console.WriteLine("Нужен тот, у кого в названии есть «Natural».");
        }

        return 0;
    }

    /// <summary>Произносит переданное — проверка озвучки без микрофона и без модели.</summary>
    private static int SayFromCommandLine(string[] args, HikaConfig config)
    {
        var index = Array.IndexOf(args, "--say");
        var text = index >= 0 && index + 1 < args.Length
            ? string.Join(' ', args[(index + 1)..])
            : "Проверка связи. Вот так я звучу.";

        using var voice = new VoiceService();
        voice.StartAsync(config.Voice, config.Speech.Language).GetAwaiter().GetResult();

        if (!voice.IsReady)
        {
            Console.WriteLine($"Говорить нечем: {voice.Description}");
            return 1;
        }

        Console.WriteLine($"Голос: {voice.Description}");
        Console.WriteLine($"Говорю: «{text}»");

        voice.Say(text);

        // Ждём, пока договорит: очередь живёт в фоновом потоке, а процесс
        // иначе закончится раньше первого звука.
        while (voice.IsSpeaking || !voice.IsReady) Thread.Sleep(50);
        Thread.Sleep(400);
        while (voice.IsSpeaking) Thread.Sleep(50);

        return 0;
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

        if (args.Contains("--list-voices")) return ListVoices(config);
        if (args.Contains("--say")) return SayFromCommandLine(args, config);

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

        // Горячие клавиши поднимаем здесь, а не внутри хоста: они должны
        // работать всё время, пока программа запущена, и переживать
        // и перенастройку, и закрытое окно настроек.
        using var hotkeys = new Interop.HotkeyListener();

        hotkeys.ListenPressed += host.BeginListening;
        hotkeys.MutePressed += () =>
        {
            host.ToggleMute();
            tray.UpdateState(host.State, host.Muted);
        };

        // Молчаливо не назначившаяся клавиша — худший из исходов: человек
        // жмёт, ничего не происходит, и понять почему нельзя.
        hotkeys.Problem += message => tray.ShowMessage("HIKA", message, ToolTipIcon.Warning);

        hotkeys.Rebind(config.Behavior.ListenHotkey, config.Behavior.MuteHotkey);

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
                        hotkeys.Rebind(updated.Behavior.ListenHotkey, updated.Behavior.MuteHotkey);
                        tray.SetPersona(updated.Persona);
                        tray.UpdateState(host.State, host.Muted);
                    },
                    onLiveListen: () => LaunchInConsole(tray, "--listen"),
                    onDiagnostics: () => LaunchInConsole(tray, "--diagnose"),
                    onTestOverlay: () => host.TestOverlay(),
                    host: host);
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

                // Сообщение об ошибке без места, где она случилась, бесполезно
                // одинаково для всех: человек не может ничего сделать,
                // а по пересланному тексту нельзя даже начать искать. Поэтому
                // первые кадры стека идут прямо в окно — их достаточно,
                // чтобы попасть в нужную строку с первого раза.
                MessageBox.Show(
                    $"Настройки не открылись.\n\n{ex.GetType().Name}: {ex.Message}\n\n" +
                    $"{TopFrames(ex)}\n\n" +
                    $"Полностью: {AppPaths.LogDirectory}",
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
            hotkeys.Rebind(updated.Behavior.ListenHotkey, updated.Behavior.MuteHotkey);
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

    /// <summary>Первые кадры стека — ровно столько, чтобы понять, где сломалось.</summary>
    private static string TopFrames(Exception ex, int count = 4)
    {
        var trace = (ex.InnerException ?? ex).StackTrace;
        if (string.IsNullOrWhiteSpace(trace)) return "";

        var lines = trace
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Take(count);

        return string.Join("\n", lines);
    }

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
              --list-voices         показать голоса для озвучки (★ — нейроголоса)
              --say ТЕКСТ           произнести текст и выйти: проверка голоса
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
