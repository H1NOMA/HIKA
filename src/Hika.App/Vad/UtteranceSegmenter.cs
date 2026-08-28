using Hika.Audio;
using Hika.Config;
using Hika.Diagnostics;

namespace Hika.Vad;

public enum SegmenterState { Silence, Speech, Trailing }

/// <summary>
/// Режет непрерывный поток звука на фразы.
///
/// Две вещи здесь сделаны намеренно и стоят пояснения.
///
/// Первая — предзапись. Детектор речи неизбежно опаздывает на пару кадров,
/// и без буфера прошлого начало слова оказывается срезанным. Для нас это
/// критично: срезается ровно то место, где звучит «Ави».
///
/// Вторая — ранняя выдача куска. Понять, что человек сказал слово пробуждения,
/// можно лишь после распознавания, а распознавание идёт по готовой фразе.
/// Значит, кайма загорелась бы уже после того, как человек договорил. Чтобы
/// свечение вспыхивало сразу, мы отдаём наружу первый кусок речи, не дожидаясь
/// её конца — по нему проверяется только слово пробуждения.
/// </summary>
public sealed class UtteranceSegmenter
{
    private readonly IVoiceActivityDetector _vad;

    private float[] _preRoll;
    private int _preRollWrite;
    private int _preRollFilled;

    private float[] _utterance;
    private int _utteranceLength;

    private int _speechSamples;
    private int _silenceSamples;
    private int _probesIssued;

    private float _enterThreshold = 0.5f;
    private float _exitThreshold = 0.275f;
    private int _silenceLimit;
    private int _minSpeech;
    private int _maxUtterance;
    private int _probeAt;
    private int _probeInterval;
    private int _probeWindow;
    private bool _probeEnabled;

    public SegmenterState State { get; private set; } = SegmenterState.Silence;
    public float LastProbability { get; private set; }
    public string VadName => _vad.Name;

    /// <summary>Началась речь. Повод зажечь едва заметное свечение — ещё не зная, к нам ли обращаются.</summary>
    public event Action? SpeechStarted;

    /// <summary>
    /// Первый кусок речи готов для ранней проверки слова пробуждения.
    /// Массив передаётся во владение получателю.
    /// </summary>
    public event Action<float[], int>? ProbeReady;

    /// <summary>Фраза закончена и достаточно длинная. Массив передаётся во владение получателю.</summary>
    public event Action<float[]>? UtteranceReady;

    /// <summary>Речь оказалась слишком короткой — щелчок, кашель, хлопок двери.</summary>
    public event Action? SpeechAborted;

    public UtteranceSegmenter(IVoiceActivityDetector vad, AudioConfig config, SpeechConfig speech)
    {
        _vad = vad;

        _preRoll = new float[Math.Max(AudioFormat.FrameSamples, AudioFormat.MsToSamples(config.PreRollMs))];
        _utterance = new float[AudioFormat.MsToSamples(config.MaxUtteranceMs) + _preRoll.Length + AudioFormat.FrameSamples];

        Reconfigure(config, speech);
    }

    public void Reconfigure(AudioConfig config, SpeechConfig speech)
    {
        _enterThreshold = config.VadThreshold;

        // Порог выхода ниже порога входа: без этого гистерезиса пауза между
        // слогами рвёт фразу на куски.
        _exitThreshold = config.VadThreshold * 0.55f;

        _silenceLimit = AudioFormat.MsToSamples(config.SilenceMs);
        _minSpeech = AudioFormat.MsToSamples(config.MinSpeechMs);
        _maxUtterance = AudioFormat.MsToSamples(config.MaxUtteranceMs);
        _probeAt = AudioFormat.MsToSamples(speech.ProbeAfterMs);
        _probeInterval = AudioFormat.MsToSamples(Math.Max(80, speech.ProbeIntervalMs));
        _probeWindow = AudioFormat.MsToSamples(Math.Max(600, speech.ProbeWindowMs));
        _probeEnabled = speech.EarlyWakeProbe;

        var rebuilt = false;

        var wantPreRoll = Math.Max(AudioFormat.FrameSamples, AudioFormat.MsToSamples(config.PreRollMs));
        if (wantPreRoll != _preRoll.Length)
        {
            _preRoll = new float[wantPreRoll];
            _preRollWrite = 0;
            _preRollFilled = 0;
            rebuilt = true;
        }

        var wantUtterance = _maxUtterance + _preRoll.Length + AudioFormat.FrameSamples;
        if (_utterance.Length < wantUtterance)
        {
            _utterance = new float[wantUtterance];
            rebuilt = true;
        }
    
        // Буферы подменились прямо посреди произносимой фразы — накопленное
        // в них больше не то, что человек говорил. Честно оборвать фразу
        // лучше, чем отправить в распознавание кусок тишины и получить
        // «услышала и ничего не сделала».
        if (rebuilt) Reset();
    }

    public void Process(ReadOnlySpan<float> frame)
    {
        var probability = _vad.Process(frame);
        LastProbability = probability;

        switch (State)
        {
            case SegmenterState.Silence:
                PushPreRoll(frame);
                if (probability >= _enterThreshold) BeginUtterance(frame);
                break;

            case SegmenterState.Speech:
                Append(frame);
                _speechSamples += frame.Length;

                if (probability < _exitThreshold)
                {
                    State = SegmenterState.Trailing;
                    _silenceSamples = frame.Length;
                }

                MaybeProbe();
                GuardMaxLength();
                break;

            case SegmenterState.Trailing:
                Append(frame);

                if (probability >= _enterThreshold)
                {
                    // Речь возобновилась — это была пауза внутри фразы, а не её конец.
                    State = SegmenterState.Speech;
                    _speechSamples += _silenceSamples + frame.Length;
                    _silenceSamples = 0;
                }
                else
                {
                    _silenceSamples += frame.Length;
                    if (_silenceSamples >= _silenceLimit) EndUtterance();
                }

                MaybeProbe();
                GuardMaxLength();
                break;
        }
    }

    private void BeginUtterance(ReadOnlySpan<float> frame)
    {
        State = SegmenterState.Speech;
        _utteranceLength = 0;
        _speechSamples = frame.Length;
        _silenceSamples = 0;
        _probesIssued = 0;

        CopyPreRollInto();
        Append(frame);

        try { SpeechStarted?.Invoke(); }
        catch (Exception ex) { Log.Error("обработчик начала речи упал", ex, "vad"); }
    }

    private void EndUtterance()
    {
        var length = _utteranceLength;
        var speech = _speechSamples;

        State = SegmenterState.Silence;
        _utteranceLength = 0;
        _speechSamples = 0;
        _silenceSamples = 0;
        _probesIssued = 0;
        _vad.Reset();

        if (speech < _minSpeech || length == 0)
        {
            try { SpeechAborted?.Invoke(); }
            catch (Exception ex) { Log.Error("обработчик обрыва речи упал", ex, "vad"); }
            return;
        }

        var copy = new float[length];
        Array.Copy(_utterance, copy, length);

        try { UtteranceReady?.Invoke(copy); }
        catch (Exception ex) { Log.Error("обработчик готовой фразы упал", ex, "vad"); }
    }

    /// <summary>
    /// Ранние проверки имени — скользящие.
    ///
    /// Раньше их было две: одна в начале, вторая заметно позже. Между ними
    /// зияла секунда, в которую имя, произнесённое не с первого слога,
    /// не находилось вовсе, — и кайма загоралась только по концу фразы.
    ///
    /// Теперь проверка повторяется каждые несколько сотен миллисекунд, пока
    /// имя не найдено. Позволяет это две вещи: во-первых, каждой проверке
    /// достаётся не вся накопленная речь, а только её последний кусок, так что
    /// стоимость не растёт вместе с фразой; во-вторых, окно кодировщика у неё
    /// своё, самое узкое. В сумме одна проверка стоит десятков миллисекунд —
    /// дешевле, чем раньше стоила одна из двух.
    /// </summary>
    private void MaybeProbe()
    {
        if (!_probeEnabled || _probesIssued >= MaxProbes) return;

        var trigger = _probeAt + _probesIssued * _probeInterval;
        if (_speechSamples < trigger) return;

        _probesIssued++;

        // Только последний кусок записи: имя звучит в начале фразы, но
        // окно шириной в полторы секунды накрывает его при любом разумном
        // темпе речи, а стоимость проверки остаётся постоянной.
        var take = Math.Min(_utteranceLength, _probeWindow);
        var from = _utteranceLength - take;

        var copy = new float[take];
        Array.Copy(_utterance, from, copy, 0, take);

        try { ProbeReady?.Invoke(copy, _probesIssued); }
        catch (Exception ex) { Log.Error("обработчик ранней проверки упал", ex, "vad"); }
    }

    /// <summary>
    /// Предел числа проверок на одну фразу.
    ///
    /// Дальше искать имя бессмысленно: если его не было в первых полутора
    /// секундах, обращались не к нам, и продолжать значит впустую занимать
    /// распознавание — то самое, которое понадобится следующей команде.
    /// </summary>
    private const int MaxProbes = 6;

    private void GuardMaxLength()
    {
        if (_speechSamples + _silenceSamples >= _maxUtterance)
        {
            Log.Debug("фраза упёрлась в предел длины, закрываю", "vad");
            EndUtterance();
        }
    }

    private void Append(ReadOnlySpan<float> frame)
    {
        var room = _utterance.Length - _utteranceLength;
        if (room <= 0) return;

        var n = Math.Min(room, frame.Length);
        frame[..n].CopyTo(_utterance.AsSpan(_utteranceLength));
        _utteranceLength += n;
    }

    private void PushPreRoll(ReadOnlySpan<float> frame)
    {
        foreach (var s in frame)
        {
            _preRoll[_preRollWrite] = s;
            _preRollWrite = (_preRollWrite + 1) % _preRoll.Length;
            if (_preRollFilled < _preRoll.Length) _preRollFilled++;
        }
    }

    private void CopyPreRollInto()
    {
        if (_preRollFilled == 0) return;

        // Кольцевой буфер разворачиваем в хронологический порядок.
        var start = (_preRollWrite - _preRollFilled + _preRoll.Length) % _preRoll.Length;
        for (int i = 0; i < _preRollFilled; i++)
        {
            _utterance[_utteranceLength++] = _preRoll[(start + i) % _preRoll.Length];
        }
    }

    public void Reset()
    {
        State = SegmenterState.Silence;
        _utteranceLength = 0;
        _speechSamples = 0;
        _silenceSamples = 0;
        _probesIssued = 0;
        _preRollWrite = 0;
        _preRollFilled = 0;
        _vad.Reset();
    }
}
