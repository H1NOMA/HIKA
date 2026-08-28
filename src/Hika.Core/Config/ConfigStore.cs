using System.Text.Json;
using Hika.Diagnostics;

namespace Hika.Config;

/// <summary>
/// Загружает конфигурацию и следит за файлом: правки применяются без перезапуска.
/// Часть настроек (модель распознавания, звуковое устройство) всё же требует
/// перезапуска подсистемы — об этом сообщает событие <see cref="Changed"/>.
/// </summary>
public sealed class ConfigStore : IDisposable
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;
    private FileSystemWatcher? _watcher;

    /// <summary>
    /// Что мы записали в файл последними.
    ///
    /// Наблюдатель за файлом не отличает чужую правку от собственной, и после
    /// каждого «Применить» настройки применялись дважды: сразу окном настроек
    /// и ещё раз через четверть секунды — по следу собственной же записи.
    /// Всё тяжёлое при этом делалось дважды: перечитывание каталога программ,
    /// перезапуск озвучки, пересборка свечения. А два пересоздания свечения
    /// внахлёст могут оставить человека вовсе без каймы до перезапуска.
    /// </summary>
    private string _lastWritten = "";

    /// <summary>Файл уже читался хотя бы раз — значит, программа работает.</summary>
    private bool _loaded;
    private DateTime _lastReload = DateTime.MinValue;
    private readonly object _lock = new();

    public HikaConfig Current { get; private set; } = new();

    /// <summary>
    /// Файла настроек не было — значит, это первый запуск.
    ///
    /// Нужно ровно для одного: показать человеку, что она умеет, не дожидаясь,
    /// пока он догадается спросить. Тот, кто ещё не знает о программе ничего,
    /// не догадается спросить её голосом — он просто закроет её через минуту.
    /// </summary>
    public bool FirstRun { get; private set; }

    /// <summary>Срабатывает после успешного перечитывания файла.</summary>
    public event Action<HikaConfig>? Changed;

    public ConfigStore(string? path = null)
    {
        _path = path ?? AppPaths.ConfigFile;
    }

    public HikaConfig Load()
    {
        lock (_lock)
        {
            try
            {
                AppPaths.EnsureCreated();

                if (!File.Exists(_path))
                {
                    Current = new HikaConfig { Version = HikaConfig.CurrentVersion };
                    FirstRun = true;
                    Save();
                    Log.Info($"создан файл настроек: {_path}", "config");
                }
                else
                {
                    var json = File.ReadAllText(_path);
                    var loaded = JsonSerializer.Deserialize<HikaConfig>(json, Options);
                    Current = loaded ?? new HikaConfig();

                    // Файл мог быть написан руками и оказаться неполным — досыпаем недостающее.
                    Normalize(Current);

                    if (Migrations.Apply(Current)) Save();
                }
            }
            catch (Exception ex)
            {
                TryBackupBroken();

                // Сбрасывать на умолчания можно только на первом чтении.
                // Дальше это уже работающая программа: файл могли поймать
                // недописанным — редактор пишет его не мгновенно, — и молча
                // обнулить из-за этого всё, что человек настраивал, значит
                // сделать хуже, чем ничего. Прежние настройки в такой момент
                // заведомо вернее прочитанного.
                if (_loaded)
                {
                    Log.Error("настройки перечитать не вышло — остаюсь на прежних", ex, "config");
                    return Current;
                }

                Log.Error("не удалось прочитать настройки, используются значения по умолчанию", ex, "config");
                Current = new HikaConfig();
            }

            _loaded = true;

            return Current;
        }
    }

    /// <summary>Сохраняет. Возвращает false, если записать не вышло.</summary>
    public bool Save()
    {
        lock (_lock)
        {
            try
            {
                AppPaths.EnsureCreated();
                var json = JsonSerializer.Serialize(Current, Options);

                // Пишем через временный файл — обрыв записи не оставит пользователя без настроек.
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, _path, overwrite: true);

                _lastWritten = json;
                return true;
            }
            catch (Exception ex)
            {
                // Молчать здесь нельзя: окно настроек до сих пор писало
                // «Сохранено» независимо от того, легло ли что-нибудь на диск,
                // и человек уходил уверенный, что настроил.
                Log.Error("не удалось сохранить настройки", ex, "config");
                return false;
            }
        }
    }

    /// <summary>
    /// Настройки строкой — ровно в том виде, в каком они лягут в файл.
    ///
    /// Нужно окну настроек, чтобы понять, есть ли несохранённые правки:
    /// сравнить снимок нынешних настроек со снимком того, что стоит
    /// в полях. Способ грубый, зато не умеет проглядеть переключатель —
    /// а перечисление полей руками умеет, и именно так теряются настройки,
    /// про которые все уверены, что они сохраняются.
    /// </summary>
    public string Snapshot()
    {
        lock (_lock)
        {
            try { return JsonSerializer.Serialize(Current, Options); }
            catch (Exception ex)
            {
                Log.Warn($"снимок настроек не снялся: {ex.Message}", "config");
                return "";
            }
        }
    }

    /// <summary>Отдельная копия настроек: правки в ней не трогают живые.</summary>
    public HikaConfig Copy()
    {
        lock (_lock)
        {
            try
            {
                var copy = JsonSerializer.Deserialize<HikaConfig>(
                    JsonSerializer.Serialize(Current, Options), Options);

                if (copy is not null) { Normalize(copy); return copy; }
            }
            catch (Exception ex)
            {
                Log.Warn($"копия настроек не сделалась: {ex.Message}", "config");
            }

            return new HikaConfig();
        }
    }

    public void StartWatching()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (string.IsNullOrEmpty(dir)) return;

            _watcher = new FileSystemWatcher(dir, Path.GetFileName(_path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };

            _watcher.Changed += OnFileTouched;
            _watcher.Created += OnFileTouched;
            _watcher.Renamed += (_, _) => OnFileTouched(null, null!);
        }
        catch (Exception ex)
        {
            Log.Warn($"слежение за файлом настроек не включилось: {ex.Message}", "config");
        }
    }

    private void OnFileTouched(object? sender, FileSystemEventArgs e)
    {
        // Редакторы дёргают файл по нескольку раз подряд — гасим дребезг.
        var now = DateTime.UtcNow;
        if ((now - _lastReload).TotalMilliseconds < 500) return;
        _lastReload = now;

        Task.Run(async () =>
        {
            await Task.Delay(250).ConfigureAwait(false); // дать редактору дописать файл

            // Своя же запись — перечитывать нечего. Проверяем содержимым,
            // а не временем: время меняется и от касания файла, а нас
            // интересует только то, изменилось ли что-нибудь по существу.
            try
            {
                var text = File.ReadAllText(_path);

                lock (_lock)
                {
                    if (text == _lastWritten)
                    {
                        Log.Debug("файл настроек изменили мы сами — перечитывать нечего", "config");
                        return;
                    }
                }
            }
            catch
            {
                // Не прочиталось — пусть разбирается Load, он умеет.
            }

            var before = Current;
            var after = Load();
            if (!ReferenceEquals(before, after))
            {
                Log.Info("настройки перечитаны", "config");
                try { Changed?.Invoke(after); }
                catch (Exception ex) { Log.Error("обработчик изменения настроек упал", ex, "config"); }
            }
        });
    }

    private void TryBackupBroken()
    {
        try
        {
            if (File.Exists(_path))
                File.Copy(_path, _path + $".broken-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
        }
        catch { /* не критично */ }
    }

    /// <summary>Приводит настройки в рабочий вид: пустые списки и бессмысленные значения чинятся.</summary>
    private static void Normalize(HikaConfig c)
    {
        c.Audio ??= new AudioConfig();
        c.Speech ??= new SpeechConfig();
        c.Wake ??= new WakeConfig();
        c.Overlay ??= new OverlayConfig();
        c.Behavior ??= new BehaviorConfig();
        c.Voice ??= new VoiceConfig();
        c.Brain ??= new BrainConfig();
        c.Learning ??= new LearningConfig();
        c.Custom ??= new List<CustomEntry>();

        if (c.Wake.Words is null || c.Wake.Words.Count == 0)
            c.Wake.Words = new List<string> { "хика", "хико", "ави" };
        c.Wake.ExtraVariants ??= new List<string>();

        if (c.Overlay.Colors is null || c.Overlay.Colors.Count < 4)
            c.Overlay.Colors = new List<string> { "#3AA0FF", "#8A6CFF", "#FF5FA2", "#31D6BC" };

        c.Audio.Gain = Math.Clamp(c.Audio.Gain, 0.1f, 12f);
        c.Audio.VadThreshold = Math.Clamp(c.Audio.VadThreshold, 0.05f, 0.95f);
        c.Audio.SilenceMs = Math.Clamp(c.Audio.SilenceMs, 150, 4000);
        c.Audio.MinSpeechMs = Math.Clamp(c.Audio.MinSpeechMs, 80, 2000);
        c.Audio.MaxUtteranceMs = Math.Clamp(c.Audio.MaxUtteranceMs, 2000, 60000);
        c.Audio.PreRollMs = Math.Clamp(c.Audio.PreRollMs, 0, 2000);

        c.Overlay.Thickness = Math.Clamp(c.Overlay.Thickness, 0.01, 0.35);
        c.Overlay.MaxOpacity = Math.Clamp(c.Overlay.MaxOpacity, 0.05, 1.0);
        c.Overlay.SensingOpacity = Math.Clamp(c.Overlay.SensingOpacity, 0.0, c.Overlay.MaxOpacity);
        c.Overlay.VoiceReactivity = Math.Clamp(c.Overlay.VoiceReactivity, 0.0, 1.0);
        c.Overlay.TargetFps = Math.Clamp(c.Overlay.TargetFps, 15, 144);

        c.Wake.Tolerance = Math.Clamp(c.Wake.Tolerance, 0.0, 0.75);
        c.Wake.StrictBelowScore = Math.Clamp(c.Wake.StrictBelowScore, 0.0, 1.0);
        c.Behavior.MatchThreshold = Math.Clamp(c.Behavior.MatchThreshold, 0.3, 0.95);
        c.Behavior.ArmedSeconds = Math.Clamp(c.Behavior.ArmedSeconds, 0, 60);
        c.Behavior.ReindexMinutes = Math.Clamp(c.Behavior.ReindexMinutes, 1, 1440);
        c.Behavior.FollowUpSeconds = Math.Clamp(c.Behavior.FollowUpSeconds, 0, 120);

        // Ранняя проверка имени. Ноль или отрицательное здесь означало бы
        // проверку на пустом звуке в бесконечном цикле — файл правят руками,
        // и опечатка в нём не должна превращаться в загруженный процессор.
        c.Speech.ProbeAfterMs = Math.Clamp(c.Speech.ProbeAfterMs, 150, 3000);
        c.Speech.ProbeIntervalMs = Math.Clamp(c.Speech.ProbeIntervalMs, 80, 2000);
        c.Speech.ProbeWindowMs = Math.Clamp(c.Speech.ProbeWindowMs, 400, 6000);
        c.Speech.Threads = Math.Clamp(c.Speech.Threads, 0, 64);

        c.Voice.Rate = Math.Clamp(c.Voice.Rate, 0.5, 2.0);
        c.Voice.Volume = Math.Clamp(c.Voice.Volume, 0.0, 1.0);

        c.Brain.MaxTokens = Math.Clamp(c.Brain.MaxTokens, 64, 8000);
        c.Brain.HistoryTurns = Math.Clamp(c.Brain.HistoryTurns, 0, 100);
        c.Brain.FollowUpSeconds = Math.Clamp(c.Brain.FollowUpSeconds, 0, 120);

        c.Learning.MaxPromptTerms = Math.Clamp(c.Learning.MaxPromptTerms, 0, 200);
        c.Learning.WakeVariantThreshold = Math.Clamp(c.Learning.WakeVariantThreshold, 1, 50);
        c.Learning.MaxBoost = Math.Clamp(c.Learning.MaxBoost, 0.0, 0.5);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}
