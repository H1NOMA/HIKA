using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hika.Diagnostics;

namespace Hika.Speech;

/// <summary>
/// Нейроголоса Microsoft — те же, что читают вслух страницы в браузере Edge.
///
/// Это лучшее, что можно получить без денег и без своей видеокарты: живая
/// интонация, правильные ударения, ничем не хуже голосов, которыми говорят
/// известные ассистенты. Работают они на серверах Microsoft, и отсюда следуют
/// два обстоятельства, о которых надо сказать прямо, а не спрятать в код.
///
/// Первое: произносимый текст уходит в интернет. Не то, что вы говорите, —
/// только то, что отвечает ассистент. Но уходит. Поэтому движок включается
/// осознанно, а не оказывается выбранным по умолчанию.
///
/// Второе: это не документированное Microsoft API, а тот же обмен, который
/// ведёт браузер. Он может перестать работать в любой день без предупреждения.
/// Поэтому неудача здесь ничего не ломает: голос просто вернётся к местному,
/// а в журнале появится строка с причиной.
/// </summary>
public sealed class EdgeSpeaker : ISpeaker
{
    private const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private const string Endpoint = "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1";
    private const string VoiceListUrl =
        "https://speech.platform.bing.com/consumer/speech/synthesize/readaloud/voices/list";

    private const string ChromeVersion = "130.0.2849.68";
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0";

    /// <summary>
    /// Голоса, известные заранее. Список с сервера точнее и полнее, но если
    /// интернета нет в момент запуска, выбирать всё равно из чего-то надо.
    /// </summary>
    private static readonly VoiceInfo[] KnownVoices =
    {
        new("ru-RU-DmitryNeural", "Дмитрий", "ru-RU", true),
        new("ru-RU-SvetlanaNeural", "Светлана", "ru-RU", true),
        new("ru-RU-DariyaNeural", "Дарья", "ru-RU", true),
        new("en-US-AndrewNeural", "Andrew", "en-US", true),
        new("en-US-AriaNeural", "Aria", "en-US", true),
        new("en-US-BrianNeural", "Brian", "en-US", true),
        new("en-US-EmmaNeural", "Emma", "en-US", true),
    };

    private readonly List<VoiceInfo> _voices = new(KnownVoices);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public string Name => "нейроголоса Microsoft";
    public bool IsReady => Current is not null;
    public VoiceInfo? Current { get; private set; }
    public IReadOnlyList<VoiceInfo> Voices => _voices;

    /// <param name="neuralOnly">
    /// Здесь ни на что не влияет: других голосов у этого движка нет —
    /// все до единого нейросетевые.
    /// </param>
    public async Task<bool> PrepareAsync(string preferredVoice, string language, bool neuralOnly, CancellationToken ct)
    {
        await RefreshVoiceListAsync(ct).ConfigureAwait(false);

        var prefix = language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "ru";

        Current =
            (!string.IsNullOrWhiteSpace(preferredVoice)
                ? _voices.FirstOrDefault(v =>
                    v.Id.Contains(preferredVoice, StringComparison.OrdinalIgnoreCase) ||
                    v.Name.Contains(preferredVoice, StringComparison.OrdinalIgnoreCase))
                : null)
            ?? _voices.FirstOrDefault(v => v.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            ?? _voices.FirstOrDefault();

        if (Current is null)
        {
            Log.Warn("нейроголоса Microsoft недоступны", "voice");
            return false;
        }

        Log.Info($"нейроголос Microsoft: {Current.Id}", "voice");
        return true;
    }

    private async Task RefreshVoiceListAsync(CancellationToken ct)
    {
        try
        {
            var url = $"{VoiceListUrl}?trustedclienttoken={TrustedClientToken}" +
                      $"&Sec-MS-GEC={SecurityToken()}&Sec-MS-GEC-Version=1-{ChromeVersion}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Authority", "speech.platform.bing.com");

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log.Debug($"список нейроголосов не получен ({(int)response.StatusCode}), беру известные", "voice");
                return;
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);

            var fetched = new List<VoiceInfo>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var shortName = item.TryGetProperty("ShortName", out var s) ? s.GetString() : null;
                var locale = item.TryGetProperty("Locale", out var l) ? l.GetString() : null;
                if (string.IsNullOrEmpty(shortName) || string.IsNullOrEmpty(locale)) continue;

                // Все прочие языки нам не нужны, а их там больше трёхсот.
                if (!locale.StartsWith("ru", StringComparison.OrdinalIgnoreCase) &&
                    !locale.StartsWith("en", StringComparison.OrdinalIgnoreCase)) continue;

                var friendly = item.TryGetProperty("FriendlyName", out var f) ? f.GetString() : null;
                fetched.Add(new VoiceInfo(shortName, ShortLabel(friendly, shortName), locale, true));
            }

            if (fetched.Count == 0) return;

            _voices.Clear();
            // Русские вперёд — их и выбирать чаще.
            _voices.AddRange(fetched.Where(v => v.Language.StartsWith("ru", StringComparison.OrdinalIgnoreCase)));
            _voices.AddRange(fetched.Where(v => !v.Language.StartsWith("ru", StringComparison.OrdinalIgnoreCase)));

            Log.Debug($"нейроголосов доступно: {_voices.Count}", "voice");
        }
        catch (Exception ex)
        {
            Log.Debug($"список нейроголосов не получен ({ex.Message}), беру известные", "voice");
        }
    }

    /// <summary>«Microsoft Dmitry Online (Natural) - Russian (Russia)» -> «Dmitry».</summary>
    private static string ShortLabel(string? friendly, string shortName)
    {
        if (!string.IsNullOrWhiteSpace(friendly))
        {
            var parts = friendly.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) return parts[1];
        }

        var dash = shortName.LastIndexOf('-');
        return dash >= 0 ? shortName[(dash + 1)..].Replace("Neural", "") : shortName;
    }

    /// <summary>
    /// Подпись запроса, которую сервер ждёт с 2024 года.
    ///
    /// Считается от текущего времени, округлённого до пяти минут, и общего
    /// для всех клиентов ключа. Округление означает, что часы компьютера
    /// должны быть выставлены верно: разъехавшиеся на десять минут часы —
    /// самая частая причина отказа.
    /// </summary>
    private static string SecurityToken()
    {
        // Смещение от эпохи Windows (1601 год) к эпохе Unix.
        const long WindowsEpochOffset = 11_644_473_600L;

        var ticks = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + WindowsEpochOffset;
        ticks -= ticks % 300;
        ticks *= 10_000_000;

        var payload = ticks.ToString(CultureInfo.InvariantCulture) + TrustedClientToken;
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(payload)));
    }

    public async Task<SynthesizedAudio?> SynthesizeAsync(string text, VoiceSettings settings, CancellationToken ct)
    {
        var voice = Current;
        if (voice is null || string.IsNullOrWhiteSpace(text)) return null;

        using var socket = new ClientWebSocket();

        var url = $"{Endpoint}?TrustedClientToken={TrustedClientToken}" +
                  $"&Sec-MS-GEC={SecurityToken()}&Sec-MS-GEC-Version=1-{ChromeVersion}";

        try
        {
            // Заголовки внутри try намеренно: часть из них .NET считает своими
            // и на попытку задать бросает исключение. Такое должно кончаться
            // возвратом к местному голосу, а не падением потока озвучки.
            socket.Options.SetRequestHeader("Pragma", "no-cache");
            socket.Options.SetRequestHeader("Cache-Control", "no-cache");
            socket.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpahbcfhjjaacgpmpbivmkgm");
            socket.Options.SetRequestHeader("User-Agent", UserAgent);
            socket.Options.SetRequestHeader("Accept-Language", "ru-RU,ru;q=0.9,en;q=0.8");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var token = timeout.Token;

            await socket.ConnectAsync(new Uri(url), token).ConfigureAwait(false);

            await SendTextAsync(socket, BuildConfigMessage(), token).ConfigureAwait(false);
            await SendTextAsync(socket, BuildSsmlMessage(text, voice.Id, settings), token).ConfigureAwait(false);

            var audio = await ReceiveAudioAsync(socket, token).ConfigureAwait(false);
            if (audio is null || audio.Length == 0) return null;

            return new SynthesizedAudio(new MemoryStream(audio, writable: false), AudioContainer.Mp3);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Warn($"нейроголос не ответил ({ex.Message}) — беру местный", "voice");
            return null;
        }
        finally
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
                        .ConfigureAwait(false);
            }
            catch { }
        }
    }

    private static string BuildConfigMessage()
        => $"X-Timestamp:{Timestamp()}\r\n" +
           "Content-Type:application/json; charset=utf-8\r\n" +
           "Path:speech.config\r\n\r\n" +
           """{"context":{"synthesis":{"audio":{"metadataoptions":{"sentenceBoundaryEnabled":"false","wordBoundaryEnabled":"false"},"outputFormat":"audio-24khz-48kbitrate-mono-mp3"}}}}""";

    private static string BuildSsmlMessage(string text, string voiceId, VoiceSettings settings)
    {
        // Скорость и громкость задаются в процентах от обычной.
        var rate = (int)Math.Round((Math.Clamp(settings.Rate, 0.5, 2.0) - 1.0) * 100);
        var volume = (int)Math.Round((Math.Clamp(settings.Volume, 0.0, 1.0) - 1.0) * 100);

        var language = voiceId.Length >= 5 ? voiceId[..5] : "ru-RU";

        var ssml =
            $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{language}'>" +
            $"<voice name='{voiceId}'>" +
            $"<prosody pitch='+0Hz' rate='{rate:+0;-0;+0}%' volume='{volume:+0;-0;+0}%'>" +
            Escape(text) +
            "</prosody></voice></speak>";

        return $"X-RequestId:{Guid.NewGuid():N}\r\n" +
               "Content-Type:application/ssml+xml\r\n" +
               $"X-Timestamp:{Timestamp()}\r\n" +
               "Path:ssml\r\n\r\n" + ssml;
    }

    private static string Escape(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;").Replace("'", "&apos;");

    private static string Timestamp()
        => DateTime.UtcNow.ToString("ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'",
            CultureInfo.InvariantCulture);

    private static Task SendTextAsync(ClientWebSocket socket, string message, CancellationToken ct)
        => socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, ct);

    /// <summary>
    /// Собирает звук из кадров.
    ///
    /// Двоичный кадр устроен так: два байта длины заголовка (старший первым),
    /// сам заголовок текстом, дальше звук. Текстовые кадры — служебные;
    /// нужный из них один: turn.end, означающий, что всё пришло.
    /// </summary>
    private static async Task<byte[]?> ReceiveAudioAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var audio = new MemoryStream();

        while (socket.State == WebSocketState.Open)
        {
            var received = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);

            if (received.MessageType == WebSocketMessageType.Close) break;

            if (received.MessageType == WebSocketMessageType.Text)
            {
                var text = Encoding.UTF8.GetString(buffer, 0, received.Count);
                if (text.Contains("Path:turn.end", StringComparison.Ordinal)) break;
                continue;
            }

            var chunk = new MemoryStream();
            chunk.Write(buffer, 0, received.Count);

            while (!received.EndOfMessage)
            {
                received = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                chunk.Write(buffer, 0, received.Count);
            }

            var bytes = chunk.ToArray();
            if (bytes.Length < 2) continue;

            var headerLength = (bytes[0] << 8) | bytes[1];
            var start = 2 + headerLength;
            if (start >= bytes.Length) continue;

            audio.Write(bytes, start, bytes.Length - start);
        }

        return audio.Length > 0 ? audio.ToArray() : null;
    }

    public void Dispose()
    {
        try { _http.Dispose(); } catch { }
    }
}
