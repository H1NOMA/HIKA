using System.Diagnostics;
using System.Text;
using Hika.Audio;
using Hika.Config;
using Hika.Diagnostics;
using Whisper.net;

namespace Hika.Stt;

/// <summary>
/// Распознавание речи через whisper.cpp.
///
/// Ключевая настройка здесь — затравка (initial prompt). Whisper заметно охотнее
/// узнаёт слова, которые видел в затравке, а «Ави» и «Хика» — не те слова,
/// которые модель ожидает услышать. Перечисляя их вместе с типичными командами
/// и названиями программ, мы поднимаем точность на этих словах ощутимо,
/// не трогая саму модель.
/// </summary>
public sealed class WhisperRecognizer : ISpeechRecognizer
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private string _description = "не загружено";
    private string _language = "ru";

    public bool IsReady => _processor is not null;
    public string Description => _description;

    /// <summary>
    /// Словарь, который подкладывается модели как контекст. Русская часть идёт
    /// первой намеренно: приоритет языка задан пользователем.
    /// </summary>
    private static string BuildPrompt(IEnumerable<string> wakeWords)
    {
        var wake = string.Join(", ", wakeWords.Select(Capitalize));

        var sb = new StringBuilder();
        sb.Append(wake).Append(". ");
        sb.Append("Привет, ").Append(wake).Append(". ");
        sb.Append("Открой, запусти, включи, найди, покажи, перейди, закрой. ");
        sb.Append("Ютуб, Гугл, Яндекс, ВКонтакте, Телеграм, Дискорд, Стим, Хром, Опера, Ворд, Эксель, Блокнот, Калькулятор, Проводник, Настройки, Спотифай, Твич, Гитхаб. ");
        sb.Append("Open, launch, start, run, close, find. ");
        sb.Append("YouTube, Google, Chrome, Word, Excel, Notepad, Explorer, Settings, Telegram, Discord, Steam, Spotify, Twitch, GitHub, VS Code.");

        return sb.ToString();
    }

    private static string Capitalize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public async Task<bool> LoadAsync(string modelPath, SpeechConfig cfg, IEnumerable<string> wakeWords)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            DisposeInner();

            var sw = Stopwatch.StartNew();

            _language = string.IsNullOrWhiteSpace(cfg.Language) ? "ru" : cfg.Language.Trim().ToLowerInvariant();

            var threads = cfg.Threads > 0
                ? cfg.Threads
                : Math.Max(2, Environment.ProcessorCount / 2);

            _factory = WhisperFactory.FromPath(modelPath);

            var builder = _factory.CreateBuilder()
                .WithLanguage(_language)
                .WithThreads(threads)
                .WithPrompt(BuildPrompt(wakeWords));

            _processor = builder.Build();

            _description = $"{Path.GetFileName(modelPath)}, язык {_language}, потоков {threads}";
            Log.Info($"распознавание готово: {_description} (загрузка {sw.ElapsedMilliseconds} мс)", "stt");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"не удалось загрузить модель распознавания: {modelPath}", ex, "stt");
            DisposeInner();
            _description = "ошибка загрузки";
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RecognitionResult> TranscribeAsync(float[] samples, CancellationToken ct = default)
    {
        var processor = _processor;
        if (processor is null || samples.Length == 0) return RecognitionResult.Empty;

        // whisper.cpp плохо переносит очень короткий вход. Добиваем тишиной до секунды:
        // модель всё равно внутри дополняет окно до тридцати секунд.
        var input = samples;
        if (samples.Length < AudioFormat.SampleRate)
        {
            input = new float[AudioFormat.SampleRate];
            samples.CopyTo(input, 0);
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var sw = Stopwatch.StartNew();
            var text = new StringBuilder();
            var detected = _language;

            await foreach (var segment in processor.ProcessAsync(input, ct).ConfigureAwait(false))
            {
                if (!string.IsNullOrWhiteSpace(segment.Text)) text.Append(segment.Text).Append(' ');
                if (!string.IsNullOrWhiteSpace(segment.Language)) detected = segment.Language;
            }

            sw.Stop();

            var raw = text.ToString().Trim();
            var cleaned = Hallucinations.Clean(raw);

            if (Hallucinations.IsLikelyHallucination(cleaned))
            {
                Log.Debug($"отброшено как выдумка модели: «{raw}»", "stt");
                return new RecognitionResult("", detected, sw.Elapsed);
            }

            return new RecognitionResult(cleaned, detected, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            return RecognitionResult.Empty;
        }
        catch (Exception ex)
        {
            Log.Error("сбой распознавания", ex, "stt");
            return RecognitionResult.Empty;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void DisposeInner()
    {
        try { _processor?.Dispose(); } catch { }
        try { _factory?.Dispose(); } catch { }
        _processor = null;
        _factory = null;
    }

    public void Dispose()
    {
        // Ждём текущее распознавание, но без гарантий: на выходе из программы
        // застрять здесь хуже, чем закрыть модель под работающим вызовом.
        var acquired = _gate.Wait(2000);

        try { DisposeInner(); }
        finally
        {
            if (acquired) _gate.Release();
            _gate.Dispose();
        }
    }
}
