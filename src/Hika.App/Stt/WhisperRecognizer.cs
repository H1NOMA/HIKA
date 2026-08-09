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

    private bool _adaptiveContext = true;
    private bool _fastDecoding = true;
    private bool _singleSegment;
    private int _audioContext;
    private int _shorterInARow;

    public bool IsReady => _processor is not null;
    public string Description => _description;

    /// <summary>Сколько своих слов сейчас подмешано в затравку.</summary>
    public int VocabularySize => _vocabulary.Length;

    /// <summary>Текущий размер окна кодировщика. Ноль — полное окно в тридцать секунд.</summary>
    public int AudioContext => _audioContext;

    /// <summary>
    /// Этот распознаватель отвечает на единственный вопрос — прозвучало ли имя.
    ///
    /// Тогда достаточно первого же куска текста: остальное всё равно
    /// не пригодится, а декодирование до конца стоит времени, которое человек
    /// проводит, глядя на неосветившийся экран.
    /// </summary>
    public bool ProbeMode
    {
        get => _singleSegment;
        set => _singleSegment = value;
    }

    /// <summary>Прогревает модель холостым проходом.</summary>
    /// <remarks>
    /// Первое распознавание всегда заметно дольше остальных: выделяется память,
    /// прогреваются кэши. Достаётся это ожидание первой же команде человека —
    /// то есть ровно тому моменту, по которому складывается впечатление
    /// о скорости всей программы. Дешевле потратить его на тишину при запуске.
    /// </remarks>
    public async Task WarmUpAsync()
    {
        if (_processor is null) return;

        try
        {
            var sw = Stopwatch.StartNew();
            await TranscribeAsync(new float[AudioFormat.SampleRate]).ConfigureAwait(false);
            Log.Debug($"модель прогрета за {sw.ElapsedMilliseconds} мс", "stt");
        }
        catch (Exception ex)
        {
            Log.Debug($"прогрев не удался: {ex.Message}", "stt");
        }
    }

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
            _threads = ResolveThreads(cfg.Threads);
            _wakeWords = wakeWords.ToArray();
            _vocabulary = vocabulary?.ToArray() ?? Array.Empty<string>();
            _adaptiveContext = cfg.AdaptiveContext;
            _fastDecoding = cfg.FastDecoding;

            // Ранняя проверка получает своё окно, самое узкое, и больше
            // не меняет его: длина куска у неё постоянна по определению.
            // Основная начинает с узкой ступени — почти любая команда в неё
            // укладывается, а если нет, окно вырастет на первой длинной фразе.
            if (_singleSegment)
            {
                _adaptiveContext = false;
                _audioContext = WhisperTuning.ProbeContext;
            }
            else
            {
                _audioContext = _adaptiveContext ? WhisperTuning.AudioContextFor(2.0) : 0;
            }

            _factory = WhisperFactory.FromPath(modelPath);
            _processor = BuildProcessor();

            _description = $"{Path.GetFileName(modelPath)}, язык {_language}, потоков {_threads}";
            if (_vocabulary.Length > 0) _description += $", своих слов {_vocabulary.Length}";
            if (_adaptiveContext) _description += ", окно по длине фразы";

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

    /// <summary>
    /// Сколько потоков отдать распознаванию.
    ///
    /// Половина ядер была осторожностью, за которую платил человек: whisper.cpp
    /// упирается ровно в арифметику, и незанятые ядра здесь — это чистое
    /// ожидание. Пары ядер, оставленных системе, хватает, чтобы всё остальное
    /// не начало заикаться; выше восьми потоков выигрыш всё равно исчезает.
    /// </summary>
    private static int ResolveThreads(int configured)
    {
        if (configured > 0) return configured;
        return Math.Clamp(Environment.ProcessorCount - 2, 2, 8);
    }

    private WhisperProcessor BuildProcessor()
    {
        var builder = _factory!.CreateBuilder()
            .WithLanguage(_language)
            .WithThreads(_threads)
            .WithPrompt(BuildPrompt(_wakeWords, _vocabulary));

        if (_audioContext > 0) builder = builder.WithAudioContextSize(_audioContext);

        if (_fastDecoding)
        {
            // Три отказа от того, что нужно расшифровке лекции и не нужно
            // команде из трёх слов.
            //
            // WithNoContext — не тащить в новую фразу текст предыдущей.
            // Для непрерывной речи это помогает, для отдельных команд только
            // сбивает: прошлая команда к нынешней отношения не имеет.
            //
            // WithTemperatureInc(0) — не переспрашивать себя. Не сойдясь
            // с порогами уверенности, whisper по умолчанию перезапускает
            // расшифровку с другой температурой, и так до пяти раз. Одна
            // трудная фраза превращается в пятикратное ожидание — ровно та
            // задержка, от которой человек и отказывается.
            //
            // BestOf(1) — один проход вместо перебора вариантов.
            builder = builder
                .WithNoContext()
                .WithTemperature(0f)
                .WithTemperatureInc(0f);

            if (builder.WithGreedySamplingStrategy() is GreedySamplingStrategyBuilder greedy)
                builder = greedy.WithBestOf(1).ParentBuilder;
        }

        if (_singleSegment)
        {
            // Проверке имени нужны первые слова, а не вся фраза. Декодировать
            // до конца — это время, которое человек проводит, глядя
            // на неосветившийся экран.
            builder = builder.WithSingleSegment().WithMaxTokensPerSegment(16);
        }

        return builder.Build();
    }

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

    /// <summary>
    /// Подгоняет окно кодировщика под длину записи.
    ///
    /// Вызывается уже под замком, прямо перед распознаванием. Пересборка
    /// обвязки стоит десятки миллисекунд, а экономит секунды — но только если
    /// случается редко, поэтому размеры ступенчатые, а уменьшение
    /// откладывается до нескольких коротких фраз подряд.
    /// </summary>
    private WhisperProcessor? AdaptContext(int samples)
    {
        if (!_adaptiveContext || _factory is null) return null;

        var wanted = WhisperTuning.AudioContextForSamples(samples, AudioFormat.SampleRate);

        if (wanted < _audioContext) _shorterInARow++;
        else _shorterInARow = 0;

        if (!WhisperTuning.ShouldSwitch(_audioContext, wanted, _shorterInARow)) return null;

        try
        {
            var previous = _audioContext;
            _audioContext = wanted;
            _shorterInARow = 0;

            var replaced = _processor;
            _processor = BuildProcessor();
            try { replaced?.Dispose(); } catch { }

            Log.Debug($"окно кодировщика: {previous} -> {wanted}", "stt");
            return _processor;
        }
        catch (Exception ex)
        {
            // Не смогли пересобрать — работаем прежней обвязкой. Медленнее,
            // но живой распознаватель важнее быстрого.
            Log.Warn($"окно кодировщика оставлено прежним: {ex.Message}", "stt");
            return null;
        }
    }

    private int _emptyInARow;

    /// <summary>
    /// Следит, не сломало ли урезанное окно само распознавание.
    ///
    /// Риск здесь ровно один и он настоящий: модель не обучалась на обрезанном
    /// окне, и теоретически может начать возвращать пустоту. Заметить это
    /// человек сможет только по тому, что программа перестала его понимать, —
    /// и подумает на что угодно, кроме размера окна.
    ///
    /// Поэтому: три подряд пустых ответа на речь, которую детектор счёл речью, —
    /// и мы навсегда возвращаемся к полному окну. Медленнее, зато работает.
    /// </summary>
    private void NoteOutcome(bool gotText)
    {
        if (gotText) { _emptyInARow = 0; return; }
        if (!_adaptiveContext || _audioContext >= WhisperTuning.FullContext) return;

        if (++_emptyInARow < 3) return;

        Log.Warn($"три пустых распознавания подряд при окне {_audioContext} — " +
                 "возвращаюсь к полному окну. Станет медленнее, но надёжнее", "stt");

        _adaptiveContext = false;
        _emptyInARow = 0;

        try
        {
            _audioContext = 0;
            var replaced = _processor;
            _processor = BuildProcessor();
            try { replaced?.Dispose(); } catch { }
        }
        catch (Exception ex)
        {
            Log.Error("возврат к полному окну не удался", ex, "stt");
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
            processor = AdaptContext(input.Length) ?? processor;

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
            NoteOutcome(raw.Length > 0);

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
