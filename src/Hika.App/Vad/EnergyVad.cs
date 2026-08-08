using Hika.Audio;

namespace Hika.Vad;

/// <summary>
/// Запасной детектор речи на одной лишь энергии сигнала.
///
/// Работает без единой зависимости и потому включается сразу, пока модель Silero
/// ещё качается или не скачалась вовсе. Честно говоря, он заметно хуже: клавиатура,
/// музыка и кулер для него звучат как речь. Держим его как страховку, а не как выбор.
/// </summary>
public sealed class EnergyVad : IVoiceActivityDetector
{
    private readonly float _threshold;
    private double _noiseFloor = 0.003;
    private int _warmupFrames;

    public string Name => "энергетический (запасной)";

    public EnergyVad(float threshold = 0.012f)
    {
        _threshold = Math.Max(0.0005f, threshold);
    }

    public float Process(ReadOnlySpan<float> frame)
    {
        if (frame.Length == 0) return 0f;

        double sum = 0;
        double peak = 0;
        int zeroCrossings = 0;
        float prev = frame[0];

        for (int i = 0; i < frame.Length; i++)
        {
            float s = frame[i];
            sum += s * s;
            peak = Math.Max(peak, Math.Abs(s));
            if ((s >= 0) != (prev >= 0)) zeroCrossings++;
            prev = s;
        }

        var rms = Math.Sqrt(sum / frame.Length);

        // Первые полсекунды считаем тишиной и калибруем по ней уровень шума.
        if (_warmupFrames < 16)
        {
            _warmupFrames++;
            _noiseFloor = _noiseFloor * 0.7 + rms * 0.3;
            return 0f;
        }

        if (rms < _noiseFloor) _noiseFloor += (rms - _noiseFloor) * 0.08;
        else _noiseFloor += (rms - _noiseFloor) * 0.0008;
        _noiseFloor = Math.Clamp(_noiseFloor, 0.0002, 0.05);

        var floor = Math.Max(_noiseFloor * 3.0, _threshold);
        if (rms < floor) return 0f;

        // Отсекаем шипение и щелчки: у речи доля переходов через ноль умеренная,
        // у согласных шумов и стука по клавишам она заметно выше.
        var zcr = zeroCrossings / (double)frame.Length;
        if (zcr > 0.35) return 0.15f;

        var confidence = Math.Clamp((rms - floor) / (floor * 4.0), 0, 1);
        return (float)(0.5 + confidence * 0.5);
    }

    public void Reset()
    {
        _warmupFrames = 0;
        _noiseFloor = 0.003;
    }

    public void Dispose() { }
}
