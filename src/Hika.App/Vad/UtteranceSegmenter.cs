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
        _probeEnabled = speech.EarlyWakeProbe;

        var wantPreRoll = Math.Max(AudioFormat.FrameSamples, AudioFormat.MsToSamples(config.PreRollMs));
        if (wantPreRoll != _preRoll.Length)
        {
            _preRoll = new float[wantPreRoll];
            _preRollWrite = 0;
            _preRollFilled = 0;
        }

        var wantUtterance = _maxUtterance + _preRoll.Length + AudioFormat.FrameSamples;
        if (_utterance.Length < wantUtterance) _utterance = new float[wantUtterance];
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

    private void MaybeProbe()
    {
        if (!_probeEnabled || _probesIssued >= 2) return;

        // Первая проверка — как только накопилось ProbeAfterMs речи.
        // Вторая — заметно позже, на случай если человек начал издалека
        // («так, э-э, Ави, открой ютуб»).
        var trigger = _probesIssued == 0 ? _probeAt : _probeAt + AudioFormat.MsToSamples(1100);
        if (_speechSamples < trigger) return;

        _probesIssued++;

        var copy = new float[_utteranceLength];
        Array.Copy(_utterance, copy, _utteranceLength);

        try { ProbeReady?.Invoke(copy, _probesIssued); }
        catch (Exception ex) { Log.Error("обработчик ранней проверки упал", ex, "vad"); }
    }

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
