namespace Hika.Stt;

public sealed record RecognitionResult(string Text, string Language, TimeSpan Elapsed)
{
    public static readonly RecognitionResult Empty = new("", "", TimeSpan.Zero);
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);
}

public interface ISpeechRecognizer : IDisposable
{
    bool IsReady { get; }
    string Description { get; }

    Task<RecognitionResult> TranscribeAsync(float[] samples, CancellationToken ct = default);
}
