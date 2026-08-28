using System.Diagnostics;
using System.Text;
using Anthropic;
using Anthropic.Models.Messages;
using Hika.Config;
using Hika.Diagnostics;
using Hika.Learning;
using Hika.Speech;

namespace Hika.Brain;

/// <summary>
/// Разговор.
///
/// Всё, что не оказалось командой, приходит сюда. Устройство здесь простое —
/// история реплик и запрос к модели, — но два решения стоит объяснить, потому
/// что без них получилось бы заметно хуже.
///
/// Ответ читается потоком и произносится по предложениям, не дожидаясь конца.
/// Разница между «сказала через полсекунды» и «сказала через шесть» — это
/// разница между собеседником и справочным терминалом, и вся она берётся
/// отсюда, а не из скорости модели.
///
/// Длина ответа ограничена жёстко, и в описании характера сказано отвечать
/// коротко. Причина не в деньгах: текст, который приятно читать, невыносимо
/// слушать. Абзац на экране пробегается глазами за секунду, а вслух звучит
/// полминуты, и перемотать его нельзя.
/// </summary>
public sealed class ClaudeBrain : IDisposable
{
    private readonly List<Turn> _history = new();
    private readonly object _lock = new();

    private AnthropicClient? _client;
    private BrainConfig _config = new();
    private string _personaName = "Хика";
    private DateTime _lastTurn = DateTime.MinValue;

    /// <summary>Разговор, брошенный на этот срок, начинается заново.</summary>
    private static readonly TimeSpan HistoryLifetime = TimeSpan.FromMinutes(20);

    private sealed record Turn(bool Mine, string Text);

    public bool IsReady => _client is not null;
    public string Description { get; private set; } = "выключен";

    /// <summary>Готовый кусок ответа, который уже можно произносить.</summary>
    public event Action<string>? ChunkReady;

    public void Configure(BrainConfig config, string personaName)
    {
        _config = config;
        _personaName = personaName;

        if (!config.Enabled)
        {
            Release();
            Description = "выключен";
            return;
        }

        var key = ApiKeyStore.Read();
        if (string.IsNullOrWhiteSpace(key))
        {
            Release();
            Description = "нет ключа";
            Log.Info("разговор включён, но ключ не задан — отвечать нечем", "brain");
            return;
        }

        try
        {
            // Пределы заданы явно, и это важнее, чем кажется. У клиента
            // по умолчанию десять минут на попытку и две повторные попытки —
            // то есть при зависшем ответе ассистент замолкает почти на полчаса,
            // потому что разговор идёт в том же потоке, что и распознавание
            // команд. Полминуты хватает любому нормальному ответу, а всё,
            // что дольше, человеку уже не нужно: он давно повторил вопрос.
            Release();

            _client = new AnthropicClient
            {
                ApiKey = key,
                Timeout = TimeSpan.FromSeconds(30),
                MaxRetries = 1,
            };
            Description = config.Model;
            Log.Info($"разговор готов: {config.Model}", "brain");
        }
        catch (Exception ex)
        {
            Release();
            Description = "ошибка";
            Log.Error("не удалось подключиться к Claude", ex, "brain");
        }
    }

    /// <summary>
    /// Спрашивает и возвращает ответ целиком. Куски ответа по мере готовности
    /// приходят в <see cref="ChunkReady"/> — их и надо озвучивать.
    /// </summary>
    public async Task<string?> AskAsync(string question, UserProfile? profile, CancellationToken ct)
    {
        var client = _client;
        if (client is null || string.IsNullOrWhiteSpace(question)) return null;

        var stopwatch = Stopwatch.StartNew();
        var full = new StringBuilder();
        var pending = new StringBuilder();

        try
        {
            var parameters = new MessageCreateParams
            {
                Model = _config.Model,
                MaxTokens = Math.Clamp(_config.MaxTokens, 64, 4000),
                System = BuildSystemPrompt(profile),
                Messages = BuildMessages(question),
            };

            await foreach (var streamEvent in client.Messages.CreateStreaming(parameters, cancellationToken: ct)
                               .ConfigureAwait(false))
            {
                if (!streamEvent.TryPickContentBlockDelta(out var delta)) continue;
                if (!delta.Delta.TryPickText(out var text)) continue;
                if (string.IsNullOrEmpty(text.Text)) continue;

                full.Append(text.Text);
                pending.Append(text.Text);

                // Как только набралось законченное предложение — отдаём его
                // в озвучку, не дожидаясь остального.
                while (SpeechText.TakeSpeakable(pending) is { } chunk)
                    Emit(chunk);
            }

            if (SpeechText.TakeSpeakable(pending, flush: true) is { } tail) Emit(tail);

            var answer = full.ToString().Trim();
            if (answer.Length == 0) return null;

            Remember(question, answer);
            Log.Info($"ответ за {stopwatch.ElapsedMilliseconds} мс, {answer.Length} знаков", "brain");
            return answer;
        }
        catch (OperationCanceledException)
        {
            // Оборвали на середине — но то, что успело прозвучать, уже
            // прозвучало. Сказать после этого «не дозвонилась» значит
            // соврать человеку, который только что слушал ответ.
            if (SpeechText.TakeSpeakable(pending, flush: true) is { } rest) Emit(rest);

            var partial = full.ToString().Trim();
            if (partial.Length == 0) return null;

            Remember(question, partial);
            Log.Warn($"ответ оборван на {partial.Length} знаках за {stopwatch.ElapsedMilliseconds} мс", "brain");

            return partial;
        }
        catch (Exception ex)
        {
            Log.Error("разговор не состоялся", ex, "brain");
            return null;
        }
    }

    private void Emit(string chunk)
    {
        try { ChunkReady?.Invoke(chunk); }
        catch (Exception ex) { Log.Error("обработчик куска ответа упал", ex, "brain"); }
    }

    private List<MessageParam> BuildMessages(string question)
    {
        var messages = new List<MessageParam>();

        lock (_lock)
        {
            // Разговор, брошенный давно, продолжать незачем: человек уже
            // не помнит, о чём была речь, а модель будет отвечать так,
            // будто помнит.
            if (DateTime.UtcNow - _lastTurn > HistoryLifetime) _history.Clear();

            foreach (var turn in _history)
            {
                messages.Add(new MessageParam
                {
                    Role = turn.Mine ? Role.Assistant : Role.User,
                    Content = turn.Text,
                });
            }
        }

        messages.Add(new MessageParam { Role = Role.User, Content = question });
        return messages;
    }

    private void Remember(string question, string answer)
    {
        lock (_lock)
        {
            _history.Add(new Turn(false, question));
            _history.Add(new Turn(true, answer));
            _lastTurn = DateTime.UtcNow;

            var limit = Math.Max(2, _config.HistoryTurns);
            while (_history.Count > limit) _history.RemoveAt(0);
        }
    }

    /// <summary>Забыть разговор — но не профиль.</summary>
    public void Forget()
    {
        lock (_lock) _history.Clear();
    }

    private string BuildSystemPrompt(UserProfile? profile)
    {
        var sb = new StringBuilder();

        sb.Append("Ты — ").Append(_personaName)
          .Append(", голосовой помощник на компьютере пользователя под Windows. ")
          .Append("Ты уже умеешь сама запускать программы и открывать сайты — это делает не модель, ")
          .Append("а сама программа, и сюда попадает только то, что командой не оказалось.\n\n");

        sb.Append("Твой ответ будет произнесён вслух синтезатором речи. Из этого следует всё остальное:\n");
        sb.Append("— отвечай коротко, две-три фразы; длиннее — только если прямо попросили подробностей;\n");
        sb.Append("— никакой разметки: ни звёздочек, ни списков, ни заголовков, ни ссылок, ни смайликов;\n");
        sb.Append("— пиши так, как это звучит вслух: не «20%», а «двадцать процентов», не «т.е.», а «то есть»;\n");
        sb.Append("— не описывай, что ты делаешь, и не переспрашивай без нужды — просто отвечай;\n");
        sb.Append("— не начинай ответ с обращения по имени и с «конечно» — вслух это звучит навязчиво.\n\n");

        sb.Append("Говори по-русски. Отвечай по-английски, только если по-английски обратились.\n");
        sb.Append("Речь распознана с микрофона, поэтому в вопросе бывают ослышки. ")
          .Append("Если смысл понятен, отвечай по смыслу и не придирайся к словам.\n");

        if (_config.ShareProfile && profile is not null)
        {
            var favourites = profile.Launches
                .OrderByDescending(kv => kv.Value)
                .Take(10)
                .Select(kv => kv.Key)
                .ToList();

            if (favourites.Count > 0)
            {
                sb.Append("\nЧем человек обычно пользуется на этом компьютере: ")
                  .Append(string.Join(", ", favourites))
                  .Append(". Это пригодится, если вопрос про его же компьютер.\n");
            }
        }

        if (!string.IsNullOrWhiteSpace(_config.Style))
            sb.Append('\n').Append(_config.Style.Trim()).Append('\n');

        return sb.ToString();
    }

    /// <summary>Проверяет ключ настоящим запросом. Для кнопки в настройках.</summary>
    public async Task<string> TestAsync(CancellationToken ct = default)
    {
        var client = _client;
        if (client is null) return ApiKeyStore.HasKey ? "не подключено" : "ключ не задан";

        try
        {
            var response = await client.Messages.Create(new MessageCreateParams
            {
                Model = _config.Model,
                MaxTokens = 32,
                Messages = [new() { Role = Role.User, Content = "Ответь одним словом: работает?" }],
            }, cancellationToken: ct).ConfigureAwait(false);

            var text = response.Content
                .Select(b => b.Value)
                .OfType<TextBlock>()
                .Select(t => t.Text)
                .FirstOrDefault();

            return string.IsNullOrWhiteSpace(text) ? "ответ пустой" : $"работает ({_config.Model})";
        }
        catch (Exception ex)
        {
            Log.Error("проверка ключа не прошла", ex, "brain");
            return $"не вышло: {ex.Message}";
        }
    }

    /// <summary>
    /// Отпускает прежнего клиента.
    ///
    /// За клиентом стоит HttpClient со своим пулом соединений, и каждое
    /// «Применить» заводило нового, не закрыв старого. Человек, который
    /// за вечер десять раз подправил стиль ответов, оставлял за собой десять
    /// пулов с живыми сокетами — до самого выхода из программы.
    /// </summary>
    private void Release()
    {
        var previous = _client;
        _client = null;

        if (previous is IDisposable disposable)
        {
            try { disposable.Dispose(); }
            catch (Exception ex) { Log.Warn($"прежнее соединение не закрылось: {ex.Message}", "brain"); }
        }
    }

    public void Dispose()
    {
        Release();
        lock (_lock) _history.Clear();
    }
}
