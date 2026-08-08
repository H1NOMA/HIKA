using System.Text;
using Hika.Audio;
using Hika.Catalog;
using Hika.Config;
using Hika.Interop;
using Hika.Nlu;
using Hika.Stt;
using Hika.Vad;
using Hika.Wake;

namespace Hika.Diagnostics;

/// <summary>
/// Проверка всей цепочки одной командой: Hika.exe --diagnose
///
/// Разработка идёт не на Windows, поэтому отчёт этой проверки — основной канал
/// обратной связи. Она нарочно печатает больше, чем нужно человеку: этот текст
/// пересылают целиком, и в нём должно быть всё, что понадобится для разбора.
/// </summary>
public static class SelfTest
{
    public static async Task<int> RunAsync(string[] args)
    {
        var report = new StringBuilder();
        void Line(string text = "") { Console.WriteLine(text); report.AppendLine(text); }

        Line("╔══════════════════════════════════════════════════════════════╗");
        Line("║  HIKA — проверка системы                                     ║");
        Line("╚══════════════════════════════════════════════════════════════╝");
        Line();

        // ---- Окружение ----
        Line("── Окружение ──────────────────────────────────────────────────");
        Line($"Сборка            : {BuildInfo.Describe()}");
        Line($"Windows           : {Environment.OSVersion.VersionString}");
        Line($".NET              : {Environment.Version}");
        Line($"Архитектура       : {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        Line($"Логических ядер   : {Environment.ProcessorCount}");
        Line($"Программа         : {Environment.ProcessPath}");
        Line($"Данные            : {AppPaths.Root}");
        Line();

        // ---- Настройки ----
        Line("── Настройки ──────────────────────────────────────────────────");
        var store = new ConfigStore();
        var config = store.Load();
        Line($"Файл              : {AppPaths.ConfigFile}");
        Line($"Слова пробуждения : {string.Join(", ", config.Wake.Words)}");
        Line($"Модель            : {config.Speech.Model} / {config.Speech.Quantization}, язык «{config.Speech.Language}»");
        Line($"Микрофон          : {(string.IsNullOrWhiteSpace(config.Audio.Device) ? "по умолчанию" : config.Audio.Device)}");
        Line($"Свечение          : {(config.Overlay.Enabled ? "включено" : "выключено")}, мониторы «{config.Overlay.Monitors}»");
        Line($"Автозапуск        : {(Startup.AutostartManager.IsEnabled() ? "включён" : "выключен")}");
        Line();

        // ---- Экраны ----
        Line("── Экраны ─────────────────────────────────────────────────────");
        foreach (var monitor in MonitorEnumerator.Enumerate())
        {
            var thickness = (int)Math.Clamp(Math.Min(monitor.Width, monitor.Height) * config.Overlay.Thickness, 24, 400);
            Line($"  {monitor}, кайма {thickness} px");
        }
        Line();

        // ---- Микрофоны ----
        Line("── Микрофоны ──────────────────────────────────────────────────");
        var devices = MicrophoneCapture.ListDevices();
        if (devices.Count == 0)
        {
            Line("  НЕ НАЙДЕНО НИ ОДНОГО МИКРОФОНА.");
            Line("  Проверьте: Параметры -> Конфиденциальность -> Микрофон -> доступ для классических приложений.");
        }
        foreach (var device in devices)
            Line($"  {(device.IsDefault ? "*" : " ")} {device.Name}");
        Line();

        // ---- Модели ----
        Line("── Модели ─────────────────────────────────────────────────────");
        var modelDirectory = WhisperModelProvider.ResolveDirectory(config.Speech);
        Line($"Папка             : {modelDirectory}");

        var vadPath = SileroModelProvider.ExpectedPath(modelDirectory);
        Line($"Детектор речи     : {(File.Exists(vadPath) ? $"на месте ({new FileInfo(vadPath).Length / 1024} КБ)" : "НЕ СКАЧАН")}");

        var whisperPath = WhisperModelProvider.FindLocal(config.Speech);
        Line($"Распознавание     : {(whisperPath is not null ? $"{Path.GetFileName(whisperPath)} ({new FileInfo(whisperPath).Length / 1_048_576} МБ)" : $"НЕ СКАЧАНО, нужна {WhisperModelProvider.DescribeChoice(config.Speech)}")}");
        Line();

        // ---- Разбор текста ----
        Line("── Разбор команд (без микрофона) ──────────────────────────────");
        var catalog = new AppCatalog();
        catalog.Load(config);
        catalog.SetInstalled(InstalledAppsScanner.Scan());
        Line($"Записей в каталоге: {catalog.Count} (встроенных {catalog.BuiltinCount}, найдено в системе {catalog.InstalledCount})");
        Line();

        var matcher = new WakeWordMatcher(config.Wake);

        string[] samples =
        {
            "Ави, открой ютуб",
            "Привет, Хика, запусти ворд",
            "Hey Avi, open Word",
            "Авиа открой гугл",
            "Ави ютуб",
            "хико запусти телеграм",
            "Ави, сделай громче",
            "Ави, сверни всё",
            "Ави",
            "открой ютуб",
            "мне надо позвонить маме",
        };

        foreach (var sample in samples)
        {
            var match = matcher.Match(sample);

            if (!match.Matched)
            {
                Line($"  «{sample}»");
                Line($"      имя не распознано — команда будет пропущена");
                continue;
            }

            Line($"  «{sample}»");
            Line($"      имя: {match.Word} (уверенность {match.Score:F2}), дальше: «{match.Rest}»");

            if (match.IsBareCall)
            {
                Line("      -> ждём команду следующей фразой");
                continue;
            }

            var intent = CommandParser.Parse(match.Rest);
            Line($"      -> {intent}");

            if (intent.Kind == IntentKind.Launch)
            {
                var top = catalog.Top(intent.Argument, 3);
                foreach (var candidate in top)
                    Line($"         {candidate.Score:F2}  {candidate.Entry.DisplayName}  [{candidate.Entry.Kind}]");
            }
        }
        Line();

        // ---- Живой микрофон ----
        var seconds = ParseSeconds(args, 6);
        if (seconds > 0)
        {
            Line($"── Проверка микрофона ({seconds} с) ────────────────────────────");
            Line("  ГОВОРИТЕ СЕЙЧАС. Скажите: «Ави, открой ютуб»");
            Line();

            await RecordAndAnalyzeAsync(config, matcher, seconds, Line).ConfigureAwait(false);
        }

        Line();
        Line("── Готово ─────────────────────────────────────────────────────");
        Line($"Журнал: {AppPaths.LogDirectory}");

        try
        {
            var reportPath = Path.Combine(AppPaths.Root, $"диагностика-{DateTime.Now:yyyy-MM-dd-HHmm}.txt");
            await File.WriteAllTextAsync(reportPath, report.ToString(), Encoding.UTF8).ConfigureAwait(false);
            Console.WriteLine();
            Console.WriteLine($"Отчёт сохранён: {reportPath}");
            Console.WriteLine("Его можно переслать целиком — в нём всё, что нужно для разбора.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Отчёт не сохранился: {ex.Message}");
        }

        return 0;
    }

    private static async Task RecordAndAnalyzeAsync(
        HikaConfig config, WakeWordMatcher matcher, int seconds, Action<string> line)
    {
        using var microphone = new MicrophoneCapture();

        if (!microphone.Start(config.Audio.Device, config.Audio.Gain))
        {
            line("  МИКРОФОН НЕ ЗАПУСТИЛСЯ. Дальше проверять нечего.");
            return;
        }

        line($"  Устройство: {microphone.ActiveDeviceName}");
        line($"  Формат входа: {microphone.SourceFormat?.SampleRate} Гц / {microphone.SourceFormat?.Channels} кан.");
        line("");

        // Детектор речи: тот, что реально будет работать.
        IVoiceActivityDetector vad;
        var vadPath = SileroModelProvider.ExpectedPath(WhisperModelProvider.ResolveDirectory(config.Speech));

        if (File.Exists(vadPath))
        {
            try { vad = new SileroVad(vadPath); }
            catch (Exception ex)
            {
                line($"  Silero не поднялся ({ex.Message}), беру энергетический детектор");
                vad = new EnergyVad(config.Audio.EnergyThreshold);
            }
        }
        else
        {
            vad = new EnergyVad(config.Audio.EnergyThreshold);
        }

        line($"  Детектор речи: {vad.Name}");
        line("");

        var meter = new LevelMeter();
        var captured = new List<float>(AudioFormat.SampleRate * (seconds + 1));
        var speechFrames = 0;
        var totalFrames = 0;
        double peak = 0;
        float maxProbability = 0;

        var lastBar = DateTime.MinValue;

        void OnFrame(float[] frame, int count)
        {
            var span = frame.AsSpan(0, count);
            meter.Process(span);
            captured.AddRange(span.ToArray());

            totalFrames++;
            peak = Math.Max(peak, meter.Rms);

            var probability = vad.Process(span);
            if (probability > maxProbability) maxProbability = probability;
            if (probability >= config.Audio.VadThreshold) speechFrames++;

            // Полоска уровня — чтобы человек сразу увидел, доходит ли до нас звук.
            if ((DateTime.UtcNow - lastBar).TotalMilliseconds > 120)
            {
                lastBar = DateTime.UtcNow;
                var filled = (int)Math.Round(meter.Normalized * 40);
                Console.Write($"\r  [{new string('#', filled)}{new string('.', 40 - filled)}] {meter.Rms:F4}   ");
            }
        }

        microphone.FrameReady += OnFrame;
        await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);
        microphone.FrameReady -= OnFrame;
        microphone.Stop();

        Console.WriteLine();
        line("");
        line($"  Кадров записано  : {totalFrames}");
        line($"  Из них с речью   : {speechFrames} ({(totalFrames > 0 ? speechFrames * 100.0 / totalFrames : 0):F0}%)");
        line($"  Пиковый уровень  : {peak:F4}");
        line($"  Уверенность в речи, максимум: {maxProbability:F3} (порог {config.Audio.VadThreshold:F2})");

        if (peak < 0.005)
        {
            line("");
            line("  ЗВУКА ПОЧТИ НЕТ. Скорее всего одно из трёх:");
            line("    - выбран не тот микрофон (список выше, нужное имя впишите в config.json);");
            line("    - микрофон приглушён в микшере Windows;");
            line("    - слишком тихий вход — поднимите audio.gain в config.json до 2.0-3.0.");
            vad.Dispose();
            return;
        }

        if (speechFrames == 0)
        {
            line("");

            // Разница принципиальная: «уверенность низкая» лечится порогом,
            // «уверенность нулевая» порогом не лечится никогда — сравнивать
            // с нулём бесполезно, сломан сам детектор.
            if (maxProbability < 0.05f)
            {
                line("  ДЕТЕКТОР РЕЧИ НЕ РАБОТАЕТ. Звук доходит, но его наибольшая");
                line("  уверенность за всю запись — практически ноль. Порог тут ни при чём:");
                line("  опускать его до нуля бессмысленно.");
                line("");
                line("  Обычно помогает удалить файл модели и дать ему скачаться заново:");
                line($"    {SileroModelProvider.ExpectedPath(WhisperModelProvider.ResolveDirectory(config.Speech))}");
                line("");
                line("  Программа в этом случае сама переходит на запасной детектор");
                line("  примерно через семь секунд разговора, так что глухой не останется.");
            }
            else
            {
                line($"  Звук есть, но до порога не дотянуло: максимум {maxProbability:F2} против {config.Audio.VadThreshold:F2}.");
                line("  Опустите порог определения речи в настройках примерно до " +
                     $"{Math.Max(0.15, maxProbability * 0.7):F2}.");
            }
        }

        // Распознавание.
        var modelPath = WhisperModelProvider.FindLocal(config.Speech);
        if (modelPath is null)
        {
            line("");
            line("  Модель распознавания не скачана — расшифровать запись нечем.");
            line("  Запустите HIKA обычным способом и дождитесь загрузки модели.");
            vad.Dispose();
            return;
        }

        line("");
        line("  Расшифровываю запись…");

        using var recognizer = new WhisperRecognizer();
        if (!await recognizer.LoadAsync(modelPath, config.Speech, matcher.Words).ConfigureAwait(false))
        {
            line("  Модель не загрузилась. Подробности в журнале.");
            vad.Dispose();
            return;
        }

        var result = await recognizer.TranscribeAsync(captured.ToArray()).ConfigureAwait(false);

        line("");
        line($"  Распознано: «{result.Text}»");
        line($"  Язык: {result.Language}, время: {result.Elapsed.TotalMilliseconds:F0} мс");

        if (result.IsEmpty)
        {
            line("");
            line("  Пусто. Либо речи в записи не было, либо модель сочла её шумом.");
        }
        else
        {
            var match = matcher.Match(result.Text);
            line("");

            if (match.Matched)
            {
                line($"  Имя услышано: {match.Word} (уверенность {match.Score:F2})");
                line($"  Команда: «{match.Rest}»");

                if (!string.IsNullOrWhiteSpace(match.Rest))
                    line($"  Намерение: {CommandParser.Parse(match.Rest)}");
            }
            else
            {
                line("  ИМЯ НЕ РАСПОЗНАНО в этой фразе.");
                line("  Если вы его произносили — впишите то, как оно записано выше,");
                line("  в wake.extraVariants в config.json. Это самый надёжный способ");
                line("  научить HIKA вашему произношению.");
            }
        }

        vad.Dispose();
    }

    private static int ParseSeconds(string[] args, int fallback)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--seconds" or "-s" && int.TryParse(args[i + 1], out var value))
                return Math.Clamp(value, 0, 60);
        }
        return fallback;
    }
}
