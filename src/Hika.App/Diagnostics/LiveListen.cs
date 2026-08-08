using Hika.Audio;
using Hika.Catalog;
using Hika.Config;
using Hika.Nlu;
using Hika.Skills;
using Hika.Stt;
using Hika.Vad;
using Hika.Wake;

namespace Hika.Diagnostics;

/// <summary>
/// Живая отладка распознавания: Hika.exe --listen
///
/// Показывает в консоли каждую услышанную фразу и то, что с ней стало —
/// узналось ли имя, с какой уверенностью, во что превратилась команда,
/// какие нашлись кандидаты в каталоге.
///
/// Нужен потому, что вопрос «почему оно меня не слышит» на самом деле
/// распадается на пять разных, и по поведению значка в трее их не различить:
/// не доходит звук; звук есть, но не считается речью; речь распознана,
/// но имя услышано иначе; имя узнано, но команда не нашлась; всё узнано,
/// но запуск не удался. Этот режим отвечает на все пять сразу.
///
/// По умолчанию ничего не запускает — только показывает. С ключом --execute
/// работает как настоящий ассистент, но с подробным выводом.
/// </summary>
public static class LiveListen
{
    public static async Task<int> RunAsync(string[] args)
    {
        var execute = args.Contains("--execute");

        var store = new ConfigStore();
        var config = store.Load();

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  HIKA — живая проверка распознавания                         ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine(BuildInfo.Describe());
        Console.WriteLine();

        if (execute) Console.WriteLine("Режим: команды ВЫПОЛНЯЮТСЯ.");
        else Console.WriteLine("Режим: только показ, ничего не запускается (--execute чтобы выполнять).");
        Console.WriteLine();

        // ---- Модели ----
        var modelDirectory = WhisperModelProvider.ResolveDirectory(config.Speech);

        Console.WriteLine($"Модель распознавания: {WhisperModelProvider.DescribeChoice(config.Speech)}");

        var modelPath = WhisperModelProvider.FindLocal(config.Speech);
        if (modelPath is null)
        {
            Console.WriteLine("Модель не скачана. Качаю — это может занять несколько минут…");
            Console.WriteLine();

            modelPath = await WhisperModelProvider.EnsureAsync(
                config.Speech,
                (fraction, text) => Console.Write($"\r  {text}   "),
                CancellationToken.None).ConfigureAwait(false);

            Console.WriteLine();

            if (modelPath is null)
            {
                Console.WriteLine();
                Console.WriteLine("НЕ УДАЛОСЬ СКАЧАТЬ МОДЕЛЬ. Без неё распознавать нечем.");
                Console.WriteLine("Проверьте интернет и попробуйте ещё раз.");
                return 1;
            }
        }

        Console.WriteLine($"Файл модели: {Path.GetFileName(modelPath)}");
        Console.Write("Загружаю модель в память… ");

        var matcher = new WakeWordMatcher(config.Wake);

        using var recognizer = new WhisperRecognizer();
        if (!await recognizer.LoadAsync(modelPath, config.Speech, matcher.Words).ConfigureAwait(false))
        {
            Console.WriteLine("НЕ ВЫШЛО.");
            Console.WriteLine("Подробности в журнале: " + AppPaths.LogDirectory);
            return 1;
        }

        Console.WriteLine("готово.");

        // ---- Детектор речи ----
        IVoiceActivityDetector vad;
        var vadPath = await SileroModelProvider.EnsureAsync(modelDirectory).ConfigureAwait(false);

        if (vadPath is not null)
        {
            // Как в настоящей работе: с наблюдателем, который заметит,
            // если нейросетевой детектор откажет молча.
            try
            {
                var resilient = new ResilientVad(new SileroVad(vadPath), config.Audio.EnergyThreshold);
                resilient.FellBack += message =>
                {
                    Console.WriteLine();
                    Console.WriteLine("  !! " + message);
                    Console.WriteLine();
                };
                vad = resilient;
            }
            catch { vad = new EnergyVad(config.Audio.EnergyThreshold); }
        }
        else
        {
            vad = new EnergyVad(config.Audio.EnergyThreshold);
        }

        Console.WriteLine($"Детектор речи: {vad.Name}");

        // ---- Каталог ----
        var catalog = new AppCatalog();
        catalog.Load(config);
        catalog.SetInstalled(InstalledAppsScanner.Scan());
        var router = new SkillRouter(catalog);

        Console.WriteLine($"Каталог: {catalog.Count} записей");
        Console.WriteLine($"Слова пробуждения: {string.Join(", ", matcher.Words)}");

        // ---- Микрофон ----
        using var microphone = new MicrophoneCapture();
        if (!microphone.Start(config.Audio.Device, config.Audio.Gain))
        {
            Console.WriteLine();
            Console.WriteLine("МИКРОФОН НЕ ЗАПУСТИЛСЯ.");
            Console.WriteLine("Список доступных: Hika.exe --list-audio");
            vad.Dispose();
            return 1;
        }

        Console.WriteLine($"Микрофон: {microphone.ActiveDeviceName}");
        Console.WriteLine($"Формат входа: {microphone.SourceFormat?.SampleRate} Гц / {microphone.SourceFormat?.Channels} кан.");
        Console.WriteLine();
        Console.WriteLine("──────────────────────────────────────────────────────────────");
        Console.WriteLine("ГОВОРИТЕ. Каждая услышанная фраза появится ниже.");
        Console.WriteLine("Выход — Ctrl+C.");
        Console.WriteLine("──────────────────────────────────────────────────────────────");
        Console.WriteLine();

        var meter = new LevelMeter();
        var segmenter = new UtteranceSegmenter(vad, config.Audio, config.Speech);

        var queue = new System.Collections.Concurrent.BlockingCollection<float[]>(8);
        var barDirty = false;
        var lastBar = DateTime.MinValue;

        segmenter.UtteranceReady += samples => queue.TryAdd(samples);

        segmenter.SpeechStarted += () =>
        {
            if (barDirty) { Console.WriteLine(); barDirty = false; }
            Console.WriteLine($"  [{DateTime.Now:HH:mm:ss}] слышу речь…");
        };

        segmenter.SpeechAborted += () =>
        {
            Console.WriteLine("             …слишком коротко, пропускаю (шум?)");
            Console.WriteLine();
        };

        microphone.FrameReady += (frame, count) =>
        {
            var span = frame.AsSpan(0, count);
            meter.Process(span);
            segmenter.Process(span);

            if ((DateTime.UtcNow - lastBar).TotalMilliseconds > 150)
            {
                lastBar = DateTime.UtcNow;
                var filled = (int)Math.Round(meter.Normalized * 30);
                Console.Write($"\r  уровень [{new string('#', filled)}{new string('.', 30 - filled)}] {meter.Rms:F4}  ");
                barDirty = true;
            }
        };

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); queue.CompleteAdding(); };

        try
        {
            foreach (var samples in queue.GetConsumingEnumerable(stop.Token))
            {
                if (barDirty) { Console.WriteLine(); barDirty = false; }

                var seconds = samples.Length / (double)AudioFormat.SampleRate;
                var result = await recognizer.TranscribeAsync(samples, stop.Token).ConfigureAwait(false);

                Console.WriteLine();
                Console.WriteLine($"  ── фраза {seconds:F1} с, распознано за {result.Elapsed.TotalMilliseconds:F0} мс ──");

                if (result.IsEmpty)
                {
                    Console.WriteLine("     УСЛЫШАНО: (ничего — либо тишина, либо модель сочла это шумом)");
                    Console.WriteLine();
                    continue;
                }

                Console.WriteLine($"     УСЛЫШАНО: «{result.Text}»");

                var match = matcher.Match(result.Text);

                if (!match.Matched)
                {
                    Console.WriteLine("     ИМЯ:      не узнано — команда была бы пропущена");
                    Console.WriteLine();
                    Console.WriteLine("     Если вы его произносили, добавьте написание выше");
                    Console.WriteLine("     в wake.extraVariants в config.json — это надёжнее");
                    Console.WriteLine("     любой подстройки порогов.");
                    Console.WriteLine();
                    continue;
                }

                Console.WriteLine($"     ИМЯ:      {match.Word} (уверенность {match.Score:F2})");

                if (match.IsBareCall)
                {
                    Console.WriteLine("     КОМАНДА:  нет — ассистент ждал бы продолжения");
                    Console.WriteLine();
                    continue;
                }

                Console.WriteLine($"     КОМАНДА:  «{match.Rest}»");

                var intent = CommandParser.Parse(match.Rest);
                Console.WriteLine($"     НАМЕРЕНИЕ:{intent}");

                if (intent.Kind == IntentKind.Launch)
                {
                    Console.WriteLine("     КАНДИДАТЫ:");
                    foreach (var candidate in catalog.Top(intent.Argument, 3))
                    {
                        var mark = candidate.Score >= config.Behavior.MatchThreshold ? "+" : " ";
                        Console.WriteLine($"       {mark} {candidate.Score:F2}  {candidate.Entry.DisplayName}  [{candidate.Entry.Kind}]");
                    }
                }

                if (execute && intent.IsActionable)
                {
                    var outcome = router.Execute(intent, config.Behavior);
                    Console.WriteLine($"     ИТОГ:     {(outcome.Success ? "выполнено" : "не вышло")} — {outcome.Description}");
                }

                Console.WriteLine();
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C — обычный способ выйти отсюда.
        }
        finally
        {
            microphone.Stop();
            vad.Dispose();
        }

        Console.WriteLine();
        Console.WriteLine("Остановлено.");
        return 0;
    }
}
