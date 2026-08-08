namespace Hika.Vad;

/// <summary>Определяет, есть ли речь в кадре звука.</summary>
public interface IVoiceActivityDetector : IDisposable
{
    /// <summary>Название движка — попадает в журнал и в отчёт диагностики.</summary>
    string Name { get; }

    /// <summary>Вероятность речи в кадре, 0..1.</summary>
    float Process(ReadOnlySpan<float> frame);

    /// <summary>Сброс внутреннего состояния между фразами.</summary>
    void Reset();
}
