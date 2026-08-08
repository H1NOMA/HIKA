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
    private DateTime _lastReload = DateTime.MinValue;
    private readonly object _lock = new();

    public HikaConfig Current { get; private set; } = new();

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
                    Current = new HikaConfig();
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
                }
            }
            catch (Exception ex)
            {
                // Битый JSON не должен мешать приложению запуститься: откатываемся к настройкам
                // по умолчанию, а испорченный файл сохраняем рядом, чтобы человек мог разобраться.
                Log.Error("не удалось прочитать настройки, используются значения по умолчанию", ex, "config");
                TryBackupBroken();
                Current = new HikaConfig();
            }

            return Current;
        }
    }

    public void Save()
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
            }
            catch (Exception ex)
            {
                Log.Error("не удалось сохранить настройки", ex, "config");
            }
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
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}
