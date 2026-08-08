namespace Hika.Audio;

/// <summary>
/// Формат, к которому приводится всё внутри HIKA.
/// 16 кГц моно — то, чего ждут и Whisper, и Silero VAD; держать что-то иное
/// означало бы пересчитывать звук дважды.
/// </summary>
public static class AudioFormat
{
    public const int SampleRate = 16000;
    public const int Channels = 1;

    /// <summary>
    /// Размер кадра. 512 отсчёта = 32 мс — ровно то окно, на котором обучен Silero VAD v5,
    /// и достаточно частый шаг, чтобы свечение успевало за голосом.
    /// </summary>
    public const int FrameSamples = 512;

    public const double FrameMs = FrameSamples * 1000.0 / SampleRate;

    public static int MsToSamples(int ms) => (int)Math.Round(ms * (SampleRate / 1000.0));
    public static double SamplesToMs(int samples) => samples * 1000.0 / SampleRate;
}
