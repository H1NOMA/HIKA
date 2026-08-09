using Hika.Diagnostics;
using Windows.Media.SpeechSynthesis;

namespace Hika.Speech;

/// <summary>
/// Голос из самой Windows.
///
/// Здесь важно понимать, что «голос Windows» — это две очень разные вещи.
///
/// Обычные голоса (Irina, David) синтезированы старым способом, склеиванием
/// кусочков записи. Они работают везде и звучат ровно так, как звучал компьютер
/// в двухтысячных: механически, с рваной интонацией, слушать их дольше минуты
/// тяжело.
///
/// Нейроголоса (те, у кого в названии стоит Natural) — это те же модели,
/// что Microsoft продаёт в облаке, только выполняющиеся прямо здесь. Разница
/// на слух огромная, интернет им не нужен, и стоят они ноль. Единственная
/// беда — Windows их не ставит сама, человеку нужно один раз зайти
/// в параметры и добавить.
///
/// Поэтому класс ищет нейроголос в первую очередь, а если не находит — честно
/// говорит об этом, вместо того чтобы молча взять механический и оставить
/// человека гадать, почему звук режет ухо.
/// </summary>
public sealed class WindowsSpeaker : ISpeaker
{
    private SpeechSynthesizer? _synthesizer;
    private readonly List<VoiceInfo> _voices = new();
    private VoiceInformation? _chosen;

    public string Name => "голос Windows";
    public bool IsReady => _synthesizer is not null && _chosen is not null;
    public VoiceInfo? Current { get; private set; }
    public IReadOnlyList<VoiceInfo> Voices => _voices;

    /// <summary>Нейроголос найден и выбран. Если нет — звук будет заметно хуже.</summary>
    public bool UsingNeural => Current?.IsNeural == true;

    public Task<bool> PrepareAsync(string preferredVoice, string language, bool neuralOnly, CancellationToken ct)
    {
        try
        {
            _synthesizer ??= new SpeechSynthesizer();

            _voices.Clear();
            var all = SpeechSynthesizer.AllVoices;

            foreach (var voice in all)
                _voices.Add(new VoiceInfo(voice.Id, voice.DisplayName, voice.Language, IsNeural(voice)));

            _chosen = Choose(all, preferredVoice, language);
            if (_chosen is null)
            {
                Log.Warn("в Windows не нашлось ни одного голоса", "voice");
                Current = null;
                return Task.FromResult(false);
            }

            var neural = IsNeural(_chosen);

            // Механический голос хуже молчания. «Хоть что-то сказала» выглядит
            // вежливым решением ровно до первой услышанной фразы.
            if (neuralOnly && !neural)
            {
                Log.Info($"нейроголосов в Windows нет (нашлось {_voices.Count} обычных) — молчу, " +
                         "чтобы не резать слух. Ставятся в Параметры -> Время и язык -> Речь -> " +
                         "Управление голосами -> Добавить голоса", "voice");
                _chosen = null;
                Current = null;
                return Task.FromResult(false);
            }

            _synthesizer.Voice = _chosen;
            Current = new VoiceInfo(_chosen.Id, _chosen.DisplayName, _chosen.Language, neural);

            Log.Info($"голос Windows: {Current.Describe()} [{Current.Language}], всего доступно {_voices.Count}", "voice");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Log.Error("голоса Windows недоступны", ex, "voice");
            return Task.FromResult(false);
        }
    }

    /// <summary>Есть ли в системе хоть один нейроголос. Для отчётов и подсказок.</summary>
    public bool HasNeuralVoice => _voices.Any(v => v.IsNeural);

    /// <summary>
    /// Нейроголос узнаётся по названию: Microsoft помечает их словом Natural
    /// (в русской локализации — «Природный»). Отдельного признака в API нет,
    /// так что других способов отличить их не существует.
    /// </summary>
    private static bool IsNeural(VoiceInformation voice)
    {
        var name = voice.DisplayName ?? "";
        return name.Contains("Natural", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Neural", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Природ", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Нейро", StringComparison.OrdinalIgnoreCase);
    }

    private static VoiceInformation? Choose(IReadOnlyList<VoiceInformation> all, string preferred, string language)
    {
        if (all.Count == 0) return null;

        // Названный человеком — вне очереди и без оговорок.
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var named = all.FirstOrDefault(v =>
                v.DisplayName.Contains(preferred, StringComparison.OrdinalIgnoreCase) ||
                v.Id.Contains(preferred, StringComparison.OrdinalIgnoreCase));

            if (named is not null) return named;
            Log.Warn($"голос «{preferred}» не найден, выбираю сама", "voice");
        }

        var prefix = language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "ru";

        // Порядок предпочтений и есть весь смысл этого метода: нужный язык
        // важнее качества (нейроголос, читающий русский текст по-английски,
        // бесполезен), а при равном языке нейроголос важнее всего остального.
        return all.FirstOrDefault(v => Lang(v, prefix) && IsNeural(v))
            ?? all.FirstOrDefault(v => Lang(v, prefix))
            ?? all.FirstOrDefault(IsNeural)
            ?? all[0];

        static bool Lang(VoiceInformation v, string prefix)
            => (v.Language ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<SynthesizedAudio?> SynthesizeAsync(string text, VoiceSettings settings, CancellationToken ct)
    {
        var synthesizer = _synthesizer;
        if (synthesizer is null || string.IsNullOrWhiteSpace(text)) return null;

        try
        {
            // Диапазон скорости в WinRT — 0.5..6.0, но всё быстрее двух
            // на слух превращается в скороговорку.
            synthesizer.Options.SpeakingRate = Math.Clamp(settings.Rate, 0.5, 2.0);
            synthesizer.Options.AudioVolume = Math.Clamp(settings.Volume, 0.0, 1.0);

            using var stream = await synthesizer.SynthesizeTextToStreamAsync(text).AsTask(ct).ConfigureAwait(false);

            // Копируем в память: поток WinRT привязан к своему объекту,
            // а звук нам нужен пережить его.
            var buffer = new MemoryStream();
            using (var source = stream.AsStreamForRead())
                await source.CopyToAsync(buffer, ct).ConfigureAwait(false);

            buffer.Position = 0;
            return new SynthesizedAudio(buffer, AudioContainer.Wave);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("синтез речи через Windows сорвался", ex, "voice");
            return null;
        }
    }

    public void Dispose()
    {
        try { _synthesizer?.Dispose(); } catch { }
        _synthesizer = null;
    }
}
