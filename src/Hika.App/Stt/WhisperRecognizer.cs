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
    private int _threads = 2;

    private string[] _wakeWords = Array.Empty<string>();
    private string[] _vocabulary = Array.Empty<string>();

    public bool IsReady => _processor is not null;
    public string Description => _description;

    /// <summary>Сколько своих слов сейчас подмешано в затравку.</summary>
    public int VocabularySize => _vocabulary.Length;

    /// <summary>
    /// Словарь, который подкладывается модели как контекст.
    ///
    /// Это единственный доступный нам способ «дообучить» распознавание, и он
    /// работает лучше, чем можно ожидать: whisper заметно охотнее слышит слова,
    /// которые видел в затравке. Личные слова идут сразу после имени и раньше
    /// общего списка — они конкретнее, а место в начале для модели весит больше.
    ///
    /// Русская часть впереди намеренно: приоритет языка задан человеком.
    /// </summary>
    private static string BuildPrompt(IEnumerable<string> wakeWords, IEnumerable<string> personal)
    {
        var wake = string.Join(", ", wakeWords.Select(Capitalize));

        var sb = new StringBuilder();
        sb.Append(wake).Append(". ");
        sb.Append("Привет, ").Append(wake).Append(". ");

        var own = personal.Where(w => !string.IsNullOrWhiteSpace(w)).Select(Capitalize).ToList();
        if (own.Count > 0) sb.Append(string.Join(", ", own)).Append(". ");

        sb.Append("Открой, запусти, включи, найди, покажи, перейди, закрой. ");
        sb.Append("Ютуб, Гугл, Яндекс, ВКонтакте, Телеграм, Дискорд, Стим, Хром, Опера, Ворд, Эксель, Блокнот, Калькулятор, Проводник, Настройки, Спотифай, Твич, Гитхаб. ");
        sb.Append("Open, launch, start, run, close, find. ");
        sb.Append("YouTube, Google, Chrome, Word, Excel, Notepad, Explorer, Settings, Telegram, Discord, Steam, Spotify, Twitch, GitHub, VS Code.");

        return sb.ToString();
    }

    private static string Capitalize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public async Task<bool> LoadAsync(string modelPath, SpeechConfig cfg, IEnumerable<string> wakeWords,
        IEnumerable<string>? vocabulary = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            DisposeInner();

            var sw = Stopwatch.StartNew();

            _language = string.IsNullOrWhiteSpace(cfg.Language) ? "ru" : cfg.Language.Trim().ToLowerInvariant();
            _threads = cfg.Threads > 0 ? cfg.Threads : Math.Max(2, Environment.ProcessorCount / 2);
            _wakeWords = wakeWords.ToArray();
            _vocabulary = vocabulary?.ToArray() ?? Array.Empty<string>();

            _factory = WhisperFactory.FromPath(modelPath);
            _processor = BuildProcessor();

            _description = $"{Path.GetFileName(modelPath)}, язык {_language}, потоков {_threads}";
            if (_vocabulary.Length > 0) _description += $", своих слов {_vocabulary.Length}";

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

    private WhisperProcessor BuildProcessor()
        => _factory!.CreateBuilder()
            .WithLanguage(_language)
            .WithThreads(_threads)
            .WithPrompt(BuildPrompt(_wakeWords, _vocabulary))
            .Build();

    /// <summary>
    /// Подменяет личный словарь в затравке.
    ///
    /// Сама модель при этом не трогается — пересобирается только обвязка
    /// вокруг неё, а это десятки миллисекунд. Но вызывать это на каждое
    /// услышанное слово всё равно не надо: пересборка встаёт в ту же очередь,
    /// что и распознавание, и в неудачный момент добавит задержки к команде.
    /// </summary>
    public async Task UpdateVocabularyAsync(IEnumerable<string> vocabulary, IEnumerable<string>? wakeWords = null)
    {
        if (_factory is null) return;

        var words = vocabulary.ToArray();
        var wake = wakeWords?.ToArray();

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_factory is null) return;

            var sameVocabulary = words.SequenceEqual(_vocabulary, StringComparer.Ordinal);
            var sameWake = wake is null || wake.SequenceEqual(_wakeWords, StringComparer.Ordinal);
            if (sameVocabulary && sameWake) return;

            _vocabulary = words;
            if (wake is not null) _wakeWords = wake;

            var previous = _processor;
            _processor = BuildProcessor();
            try { previous?.Dispose(); } catch { }

            Log.Info($"словарь распознавания обновлён: своих слов {words.Length}", "stt");
        }
        catch (Exception ex)
        {
            Log.Error("не удалось обновить словарь распознавания", ex, "stt");
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
