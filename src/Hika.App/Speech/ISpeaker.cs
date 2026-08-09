namespace Hika.Speech;

/// <summary>Голос, доступный для озвучки.</summary>
public sealed record VoiceInfo(string Id, string Name, string Language, bool IsNeural)
{
    public string Describe() => IsNeural ? $"{Name} (нейро)" : Name;
}

/// <summary>
/// Умение произнести текст вслух.
///
/// Реализаций две, и различаются они не кодом, а тем, откуда берётся голос:
/// из самой Windows или из интернета. Интерфейс существует ровно затем, чтобы
/// одно можно было заменить другим, когда первое не справилось, —
/// а не ради абстракции как таковой.
/// </summary>
public interface ISpeaker : IDisposable
{
    /// <summary>Название движка для журнала и окна настроек.</summary>
    string Name { get; }

    /// <summary>Готов ли говорить. Пока false, обращаться бессмысленно.</summary>
    bool IsReady { get; }

    /// <summary>Какой голос выбран сейчас.</summary>
    VoiceInfo? Current { get; }

    /// <summary>Все голоса, из которых можно выбрать.</summary>
    IReadOnlyList<VoiceInfo> Voices { get; }

    /// <summary>
    /// Синтезирует речь. Возвращает готовый звук в виде потока, но не
    /// проигрывает его: воспроизведение общее для всех движков и живёт отдельно.
    /// Null означает, что движок не справился и надо пробовать следующий.
    /// </summary>
    Task<SynthesizedAudio?> SynthesizeAsync(string text, VoiceSettings settings, CancellationToken ct);

    /// <summary>
    /// Перечитать список голосов и выбрать подходящий.
    /// </summary>
    /// <param name="neuralOnly">
    /// Согласиться только на нейроголос. Не нашлось такого — вернуть false
    /// и промолчать, а не брать механический.
    /// </param>
    Task<bool> PrepareAsync(string preferredVoice, string language, bool neuralOnly, CancellationToken ct);
}

/// <summary>Готовый звук и то, как его читать.</summary>
public sealed record SynthesizedAudio(Stream Data, AudioContainer Container) : IDisposable
{
    public void Dispose() => Data.Dispose();
}

public enum AudioContainer
{
    /// <summary>WAV с заголовком RIFF.</summary>
    Wave,

    /// <summary>MP3.</summary>
    Mp3,
}

public sealed record VoiceSettings(double Rate, double Volume);
