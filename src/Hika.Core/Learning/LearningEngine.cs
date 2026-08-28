using Hika.Config;
using Hika.Diagnostics;
using Hika.Nlu;

namespace Hika.Learning;

/// <summary>
/// Куда подмешиваются знания о человеке при поиске по каталогу.
/// Реализуется <see cref="LearningEngine"/>, а каталогу достаточно этих двух вопросов.
/// </summary>
public interface IEntryPrior
{
    /// <summary>Выученный синоним: если эту фразу уже связывали с записью — вернёт её идентификатор.</summary>
    string? AliasTarget(string phrase);

    /// <summary>Прибавка к оценке за то, что запись уже запускали. Всегда маленькая.</summary>
    double Boost(string entryId);
}

/// <summary>
/// Всё обучение целиком: что помнить, когда учить и что из этого показывать
/// остальным частям программы.
///
/// Устроено вокруг одного наблюдения: самый полезный сигнал человек даёт сам,
/// не зная об этом. Когда команда не сработала, он почти всегда повторяет её
/// иначе — и вторая, удавшаяся попытка объясняет первую лучше любого словаря.
/// Достаточно заметить эту пару и связать одно с другим.
///
/// Отдельно стоит сказать, чего здесь нет. Нет дообучения самой модели
/// распознавания: на домашнем компьютере это невозможно, и делать вид,
/// что мы этим занимаемся, нечестно. Есть словарь-подсказка, который модель
/// читает перед каждой фразой, — и на слух он даёт похожий результат.
/// </summary>
public sealed class LearningEngine : IEntryPrior, IDisposable
{
    private readonly ProfileStore _store;
    private readonly SpeechJournal _journal;
    private LearningConfig _config;

    /// <summary>
    /// Тот же замок, под которым профиль пишется на диск. Общий намеренно:
    /// иначе запись застаёт словарь посреди изменения.
    /// </summary>
    private readonly object _lock;

    // Неудача ждёт объяснения. Если следом почти та же фраза сработает —
    // значит, человек сам показал, что имел в виду.
    private string? _pendingFailure;
    private DateTime _pendingFailureAt = DateTime.MinValue;

    /// <summary>Сколько ждать исправления после неудачной команды.</summary>
    private static readonly TimeSpan CorrectionWindow = TimeSpan.FromSeconds(30);

    /// <summary>Появилось новое написание имени — его стоит добавить в слова пробуждения.</summary>
    public event Action<string>? WakeVariantLearned;

    /// <summary>Словарь подсказок заметно изменился — распознаванию пора его перечитать.</summary>
    public event Action<IReadOnlyList<string>>? VocabularyChanged;

    public UserProfile Profile => _store.Profile;
    public string JournalPath => _journal.Path;

    private int _termsAtLastPublish;

    public LearningEngine(LearningConfig config, ProfileStore? store = null, SpeechJournal? journal = null)
    {
        _config = config;
        _store = store ?? new ProfileStore();
        _journal = journal ?? new SpeechJournal();
        _lock = _store.Gate;
    }

    public void Start()
    {
        _store.Load();
        _termsAtLastPublish = _store.Profile.Terms.Count;
    }

    public void Reconfigure(LearningConfig config) => _config = config;

    // ---- То, что спрашивает каталог ----------------------------------------

    public string? AliasTarget(string phrase)
    {
        if (!_config.Enabled) return null;

        var key = TextNormalizer.Normalize(phrase).Trim();
        if (key.Length == 0) return null;

        lock (_lock)
        {
            return _store.Profile.Aliases.TryGetValue(key, out var alias) ? alias.EntryId : null;
        }
    }

    public double Boost(string entryId)
    {
        if (!_config.Enabled) return 0;
        lock (_lock) return Adaptation.LaunchBoost(_store.Profile, entryId, _config.MaxBoost);
    }

    // ---- То, что сообщает ведущий -------------------------------------------

    /// <summary>Услышали фразу и разобрались, что с ней вышло.</summary>
    public void Observe(JournalEntry entry, string commandPhrase)
    {
        if (!_config.Enabled) return;

        lock (_lock)
        {
            var profile = _store.Profile;

            Adaptation.Observe(profile, entry.Text, entry.Success);

            if (!string.IsNullOrWhiteSpace(commandPhrase))
            {
                profile.Commands++;

                if (entry.Success)
                {
                    if (!string.IsNullOrEmpty(entry.EntryId))
                        Adaptation.RememberLaunch(profile, entry.EntryId);
                    else
                        profile.Successes++;

                    TryLearnCorrection(commandPhrase, entry.EntryId, entry.Intent);
                    _pendingFailure = null;
                }
                else
                {
                    _pendingFailure = commandPhrase;
                    _pendingFailureAt = DateTime.UtcNow;
                }
            }

            _store.Touch();
            PublishVocabularyIfGrown();
        }

        if (_config.KeepJournal) _journal.Append(entry);
    }

    /// <summary>
    /// Связывает предыдущую неудачу с нынешней удачей.
    ///
    /// Вызывается уже под замком.
    /// </summary>
    private void TryLearnCorrection(string succeededPhrase, string entryId, string entryName)
    {
        if (!_config.LearnAliases) return;
        if (_pendingFailure is null || string.IsNullOrEmpty(entryId)) return;
        if (DateTime.UtcNow - _pendingFailureAt > CorrectionWindow) return;

        var failed = _pendingFailure;
        _pendingFailure = null;

        // Та же самая фраза во второй раз — это не синоним, а просто повтор
        // после того, как программа наконец расслышала.
        if (string.Equals(TextNormalizer.Normalize(failed), TextNormalizer.Normalize(succeededPhrase),
                StringComparison.Ordinal))
            return;

        if (Adaptation.LearnAlias(_store.Profile, failed, succeededPhrase, entryId, entryName))
            Log.Info($"запомнила: «{failed}» — это {entryName}", "learn");
    }

    /// <summary>
    /// Имя услышано, но неуверенно. Если так повторяется, надо принять
    /// это написание как своё.
    /// </summary>
    public void ObserveWakeAttempt(string heard, double score)
    {
        if (!_config.Enabled || !_config.LearnWakeVariants) return;

        string? confirmed;
        lock (_lock)
        {
            confirmed = Adaptation.ObserveWakeVariant(_store.Profile, heard, score, _config.WakeVariantThreshold);
            _store.Touch();
        }

        if (confirmed is null) return;

        Log.Info($"вы зовёте меня «{confirmed}» — принимаю это написание", "learn");
        try { WakeVariantLearned?.Invoke(confirmed); }
        catch (Exception ex) { Log.Error("обработчик нового написания имени упал", ex, "learn"); }
    }

    /// <summary>Словарь подсказок для распознавания: свои слова плюс всё, что оно уже знает.</summary>
    public IReadOnlyList<string> Vocabulary()
    {
        if (!_config.Enabled) return Array.Empty<string>();
        lock (_lock) return Adaptation.PromptTerms(_store.Profile, _config.MaxPromptTerms);
    }

    /// <summary>Написания имени, набравшие достаточно повторов.</summary>
    public IReadOnlyList<string> WakeVariants()
    {
        if (!_config.Enabled || !_config.LearnWakeVariants) return Array.Empty<string>();
        lock (_lock) return Adaptation.ConfirmedWakeVariants(_store.Profile, _config.WakeVariantThreshold);
    }

    /// <summary>
    /// Переносить словарь в распознавание на каждое слово нельзя: это
    /// пересборка процессора модели. Ждём, пока накопится заметная разница.
    /// </summary>
    private void PublishVocabularyIfGrown()
    {
        var count = _store.Profile.Terms.Count;
        if (count - _termsAtLastPublish < 8) return;
        _termsAtLastPublish = count;

        var vocabulary = Adaptation.PromptTerms(_store.Profile, _config.MaxPromptTerms);
        try { VocabularyChanged?.Invoke(vocabulary); }
        catch (Exception ex) { Log.Error("обработчик обновлённого словаря упал", ex, "learn"); }
    }

    /// <summary>Добавляет синоним руками — из окна настроек.</summary>
    public void AddAlias(string phrase, string entryId, string entryName)
    {
        var key = TextNormalizer.Normalize(phrase).Trim();
        if (key.Length == 0 || string.IsNullOrWhiteSpace(entryId)) return;

        lock (_lock)
        {
            _store.Profile.Aliases[key] = new AliasStat
            {
                EntryId = entryId,
                EntryName = entryName,
                Count = 1,
                Manual = true,
                LastSeen = DateTime.UtcNow,
            };
            _store.Touch();
        }
    }

    public void RemoveAlias(string phrase)
    {
        var key = TextNormalizer.Normalize(phrase).Trim();
        lock (_lock)
        {
            if (_store.Profile.Aliases.Remove(key)) _store.Touch();
        }
    }

    /// <summary>Короткая сводка для окна настроек: что программа успела узнать.</summary>
    public string Describe()
    {
        lock (_lock)
        {
            var p = _store.Profile;
            if (p.Utterances == 0) return "Пока ничего — я вас ещё не слышала.";

            var days = Math.Max(1, (int)(DateTime.UtcNow - p.Since).TotalDays);
            var parts = new List<string>
            {
                $"услышано фраз: {p.Utterances}",
                $"слов в словаре: {Math.Min(p.Terms.Count, _config.MaxPromptTerms)} из {p.Terms.Count}",
            };

            if (p.Aliases.Count > 0) parts.Add($"выучено синонимов: {p.Aliases.Count}");
            if (p.Launches.Count > 0) parts.Add($"знакомых программ: {p.Launches.Count}");

            var variants = Adaptation.ConfirmedWakeVariants(p, _config.WakeVariantThreshold);
            if (variants.Count > 0) parts.Add($"написаний имени: {string.Join(", ", variants.Take(4))}");

            if (p.Commands > 0) parts.Add($"команд выполнено: {p.SuccessRate:P0}");

            return string.Join(", ", parts) + $" (наблюдаю {days} дн.)";
        }
    }

    /// <summary>Забыть всё.</summary>
    public void Forget()
    {
        lock (_lock)
        {
            _store.Reset();
            _termsAtLastPublish = 0;
            _pendingFailure = null;
        }
    }

    /// <summary>Пересобрать профиль из дневника речи — если правила обучения поменялись.</summary>
    public void RebuildFromJournal()
    {
        var rebuilt = _journal.Rebuild();
        lock (_lock)
        {
            var current = _store.Profile;
            current.Terms = rebuilt.Terms;
            current.Launches = rebuilt.Launches;
            current.Utterances = rebuilt.Utterances;
            current.Successes = rebuilt.Successes;
            _store.Touch();
        }
        _store.Flush();
    }

    public void Dispose()
    {
        try { _store.Dispose(); } catch { }
        try { _journal.Dispose(); } catch { }
    }
}
