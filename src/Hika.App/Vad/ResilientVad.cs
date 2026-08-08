using Hika.Audio;
using Hika.Diagnostics;

namespace Hika.Vad;

/// <summary>
/// Детектор речи, который замечает собственную неисправность.
///
/// Написан после случая, стоившего целого круга переписки: нейросетевой
/// детектор из-за тонкости в формате входа стабильно отвечал «речи нет»
/// при совершенно исправном звуке. Снаружи это выглядело как полностью
/// глухой ассистент, и никакая настройка порогов не помогала — потому что
/// порог сравнивался с нулём.
///
/// Вывод общий: если подсистема может отказать молча, она обязана уметь
/// это заметить. Здесь достаточно простого наблюдения — когда микрофон
/// уверенно слышит громкий звук много секунд подряд, а детектор ни разу
/// даже не приблизился к порогу, дело не в тихой речи, а в самом детекторе.
/// Тогда мы переключаемся на грубый энергетический и говорим об этом вслух.
/// Работать хуже — лучше, чем не работать вовсе.
/// </summary>
public sealed class ResilientVad : IVoiceActivityDetector
{
    /// <summary>Громкость, выше которой в кадре точно что-то есть.</summary>
    private const double LoudRms = 0.02;

    /// <summary>Сколько громких кадров подряд терпеть молчание детектора. 220 кадров ≈ 7 секунд.</summary>
    private const int PatienceFrames = 220;

    /// <summary>Ниже этого детектор явно не работает: на речи он выдаёт куда больше.</summary>
    private const float AliveProbability = 0.3f;

    private readonly IVoiceActivityDetector _primary;
    private readonly EnergyVad _fallback;

    private bool _switched;
    private int _loudFrames;
    private float _bestSeen;

    /// <summary>Сработал переход на запасной детектор. Повод сказать об этом человеку.</summary>
    public event Action<string>? FellBack;

    public string Name => _switched
        ? $"{_fallback.Name} — {_primary.Name} не отвечал"
        : _primary.Name;

    public float MaxProbabilitySeen => _bestSeen;
    public bool UsingFallback => _switched;

    public ResilientVad(IVoiceActivityDetector primary, float energyThreshold)
    {
        _primary = primary;
        _fallback = new EnergyVad(energyThreshold);
    }

    public float Process(ReadOnlySpan<float> frame)
    {
        // Запасной кормим всегда, даже пока он не нужен: его уровень шума
        // калибруется по тишине, и включённый в разгар речи он был бы бесполезен.
        var fallbackProbability = _fallback.Process(frame);
        if (_switched) return fallbackProbability;

        var probability = _primary.Process(frame);
        if (probability > _bestSeen) _bestSeen = probability;

        if (Rms(frame) < LoudRms)
        {
            // Тихо — сказать нечего, счётчик терпения не двигаем.
            return probability;
        }

        if (_bestSeen >= AliveProbability)
        {
            // Детектор хоть раз показал, что живой. Больше не следим.
            _loudFrames = 0;
            return probability;
        }

        if (++_loudFrames < PatienceFrames) return probability;

        _switched = true;

        var message =
            $"Детектор речи не реагирует на звук: за {PatienceFrames * AudioFormat.FrameMs / 1000:F0} с " +
            $"громкого сигнала его наибольшая уверенность — {_bestSeen:F2}. " +
            "Перехожу на запасной, распознавание продолжит работать.";

        Log.Warn(message, "vad");

        try { FellBack?.Invoke(message); }
        catch (Exception ex) { Log.Error("обработчик перехода на запасной детектор упал", ex, "vad"); }

        return fallbackProbability;
    }

    private static double Rms(ReadOnlySpan<float> frame)
    {
        if (frame.Length == 0) return 0;

        double sum = 0;
        for (int i = 0; i < frame.Length; i++) sum += frame[i] * frame[i];

        return Math.Sqrt(sum / frame.Length);
    }

    public void Reset()
    {
        _primary.Reset();
        _fallback.Reset();

        // Счётчик терпения намеренно не сбрасываем: он про исправность
        // детектора вообще, а не про отдельную фразу.
    }

    public void Dispose()
    {
        _primary.Dispose();
        _fallback.Dispose();
    }
}
