using System.Text.Json;
using Hika.Config;
using Hika.Diagnostics;

namespace Hika.Learning;

/// <summary>
/// Хранит профиль на диске.
///
/// Запись отложенная и через временный файл. Причина простая: профиль меняется
/// после каждой фразы, а фразы идут подряд — писать файл на каждое слово значит
/// нагружать диск ради данных, которые всё равно перезапишутся через секунду.
/// Временный файл с переименованием защищает от главного: выключения питания
/// посреди записи. Потерять последнюю минуту наблюдений не жалко, потерять
/// весь накопленный словарь — жалко очень.
/// </summary>
public sealed class ProfileStore : IDisposable
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;
    private readonly object _lock = new();

    /// <summary>
    /// Замок, под которым живёт профиль.
    ///
    /// Отдаётся наружу намеренно. Профиль правит наблюдатель за речью,
    /// а на диск его пишет здешний таймер — и пока у каждого был свой замок,
    /// запись могла застать словарь посреди изменения. Сериализация в этот
    /// момент срывается с ошибкой «коллекция изменилась», ошибка уходит
    /// в журнал, а профиль не сохраняется вовсе — то есть обучение молча
    /// перестаёт работать между запусками.
    /// </summary>
    public object Gate => _lock;
    private readonly System.Threading.Timer _flushTimer;

    private bool _dirty;
    private bool _disposed;

    public UserProfile Profile { get; private set; } = new();

    public ProfileStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppPaths.Root, "profile.json");
        _flushTimer = new System.Threading.Timer(_ => Flush(), null,
            TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));
    }

    public UserProfile Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_path))
                {
                    var loaded = JsonSerializer.Deserialize<UserProfile>(File.ReadAllText(_path), Options);
                    if (loaded is not null)
                    {
                        Profile = loaded;
                        Profile.Terms ??= new Dictionary<string, TermStat>(StringComparer.Ordinal);
                        Profile.Aliases ??= new Dictionary<string, AliasStat>(StringComparer.Ordinal);
                        Profile.WakeVariants ??= new Dictionary<string, int>(StringComparer.Ordinal);
                        Profile.Launches ??= new Dictionary<string, int>(StringComparer.Ordinal);

                        Log.Info($"профиль: слов {Profile.Terms.Count}, синонимов {Profile.Aliases.Count}, " +
                                 $"фраз услышано {Profile.Utterances}", "learn");
                        return Profile;
                    }
                }
            }
            catch (Exception ex)
            {
                // Битый профиль — не повод не запуститься. Начинаем заново,
                // а испорченный откладываем: вдруг человек захочет посмотреть.
                Log.Error("профиль не прочитался, начинаю наблюдения заново", ex, "learn");
                try { File.Move(_path, _path + ".broken", overwrite: true); } catch { }
            }

            Profile = new UserProfile();
            return Profile;
        }
    }

    /// <summary>Пометить, что профиль изменился. Запись произойдёт сама, не сразу.</summary>
    public void Touch() => _dirty = true;

    public void Flush()
    {
        if (!_dirty || _disposed) return;

        lock (_lock)
        {
            if (!_dirty) return;
            _dirty = false;

            try
            {
                AppPaths.EnsureCreated();
                Adaptation.Prune(Profile);

                var temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(Profile, Options));

                if (File.Exists(_path)) File.Replace(temp, _path, null);
                else File.Move(temp, _path);
            }
            catch (Exception ex)
            {
                Log.Error("профиль не сохранился", ex, "learn");
            }
        }
    }

    /// <summary>Забыть всё. Возврат к состоянию первого запуска.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            Profile = new UserProfile();
            _dirty = true;
        }
        Flush();
        Log.Info("профиль очищен по просьбе человека", "learn");
    }

    public void Dispose()
    {
        _flushTimer.Dispose();
        Flush();
        _disposed = true;
    }
}
