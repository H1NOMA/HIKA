namespace Hika.Audio;

/// <summary>
/// Следящий за огибающей голоса измеритель. Его выход — то, что заставляет
/// кайму мерцать в такт речи.
///
/// Отдельные времена нарастания и спада принципиальны: свечение должно
/// вспыхивать на слоге мгновенно и гаснуть плавно. Одинаковые времена дают
/// либо дёрганую, либо вялую картинку — и то и другое выглядит дёшево.
/// </summary>
public sealed class LevelMeter
{
    private readonly double _attack;
    private readonly double _release;

    private double _envelope;
    private double _noiseFloor = 0.004;
    private double _peak = 0.05;

    /// <summary>Сырое среднеквадратичное значение последнего кадра, 0..1.</summary>
    public double Rms { get; private set; }

    /// <summary>Сглаженная огибающая, 0..1. Для отображения брать именно её.</summary>
    public double Envelope => _envelope;

    /// <summary>
    /// Огибающая, растянутая между уровнем шума и недавним пиком, 0..1.
    /// Благодаря этому свечение одинаково живо реагирует и на шёпот, и на крик,
    /// и не зависит от того, насколько громкий у человека микрофон.
    /// </summary>
    public double Normalized { get; private set; }

    /// <param name="attackMs">Время нарастания. Малое — резкая реакция на слог.</param>
    /// <param name="releaseMs">Время спада. Большое — мягкое затухание.</param>
    public LevelMeter(double attackMs = 18, double releaseMs = 220)
    {
        _attack = Math.Exp(-AudioFormat.FrameMs / Math.Max(1.0, attackMs));
        _release = Math.Exp(-AudioFormat.FrameMs / Math.Max(1.0, releaseMs));
    }

    public void Process(ReadOnlySpan<float> frame)
    {
        if (frame.Length == 0) return;

        double sum = 0;
        for (int i = 0; i < frame.Length; i++)
        {
            double s = frame[i];
            sum += s * s;
        }

        Rms = Math.Sqrt(sum / frame.Length);

        var coeff = Rms > _envelope ? _attack : _release;
        _envelope = Rms + coeff * (_envelope - Rms);

        // Уровень шума ползёт вверх медленно и вниз быстро: так он находит настоящую
        // тишину в комнате и не «залипает» после случайного грохота.
        if (Rms < _noiseFloor) _noiseFloor += (Rms - _noiseFloor) * 0.05;
        else _noiseFloor += (Rms - _noiseFloor) * 0.0006;
        _noiseFloor = Math.Clamp(_noiseFloor, 0.0002, 0.08);

        // Пик спадает сам, иначе один громкий звук навсегда придавит чувствительность.
        _peak = Math.Max(_peak * 0.9985, _envelope);
        _peak = Math.Clamp(_peak, _noiseFloor * 4, 1.0);

        var span = Math.Max(1e-6, _peak - _noiseFloor);
        Normalized = Math.Clamp((_envelope - _noiseFloor) / span, 0, 1);

        // Слегка выгибаем кривую: голос почти всё время держится в нижней трети
        // диапазона, и без этого кайма выглядит бледной.
        Normalized = Math.Pow(Normalized, 0.62);
    }

    public void Reset()
    {
        _envelope = 0;
        Normalized = 0;
        Rms = 0;
    }
}
