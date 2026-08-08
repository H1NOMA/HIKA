using System.Text;
using System.Text.Json;
using Hika.Config;
using Hika.Diagnostics;

namespace Hika.Learning;

/// <summary>
/// Дневник речи: всё услышанное, по строке на фразу.
///
/// Это то самое «записывай и учись» в буквальном смысле. Файл нужен для двух
/// вещей. Во-первых, из него в любой момент можно пересобрать профиль заново —
/// если правила обучения изменятся, история не пропадёт. Во-вторых, человек
/// может его открыть и своими глазами увидеть, что программа про него знает;
/// обучение, которое нельзя посмотреть, доверия не заслуживает.
///
/// Формат — по объекту JSON на строку: дописывать дёшево, читать можно кусками,
/// а обрыв записи портит одну строку вместо всего файла.
/// </summary>
public sealed class SpeechJournal : IDisposable
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Больше десяти мегабайт дневника не нужно никому.</summary>
    private const long MaxBytes = 10L * 1024 * 1024;

    private readonly string _path;
    private readonly object _lock = new();
    private StreamWriter? _writer;

    public SpeechJournal(string? path = null)
        => _path = path ?? System.IO.Path.Combine(AppPaths.Root, "речь.jsonl");

    public string Path => _path;

    public void Append(JournalEntry entry)
    {
        lock (_lock)
        {
            try
            {
                _writer ??= Open();
                _writer.WriteLine(JsonSerializer.Serialize(entry, Options));
                _writer.Flush();

                if (_writer.BaseStream.Length > MaxBytes) Rotate();
            }
            catch (Exception ex)
            {
                Log.Warn($"дневник речи не пишется: {ex.Message}", "learn");
            }
        }
    }

    private StreamWriter Open()
    {
        AppPaths.EnsureCreated();
        return new StreamWriter(new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(false));
    }

    private void Rotate()
    {
        try
        {
            _writer?.Dispose();
            _writer = null;
            File.Move(_path, _path + ".old", overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn($"дневник речи не переоткрылся: {ex.Message}", "learn");
        }
    }

    /// <summary>Читает дневник целиком. Битые строки пропускаются молча — так и задумано.</summary>
    public IEnumerable<JournalEntry> Read()
    {
        if (!File.Exists(_path)) yield break;

        foreach (var line in File.ReadLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JournalEntry? entry = null;
            try { entry = JsonSerializer.Deserialize<JournalEntry>(line, Options); }
            catch { }

            if (entry is not null) yield return entry;
        }
    }

    /// <summary>
    /// Пересобирает профиль из дневника с нуля. Нужно, когда правила обучения
    /// поменялись, а история осталась.
    /// </summary>
    public UserProfile Rebuild()
    {
        var profile = new UserProfile();
        var count = 0;

        foreach (var entry in Read())
        {
            if (string.IsNullOrWhiteSpace(entry.Text)) continue;

            Adaptation.Observe(profile, entry.Text, entry.Success);
            if (entry.Success && !string.IsNullOrEmpty(entry.EntryId))
                Adaptation.RememberLaunch(profile, entry.EntryId);

            count++;
        }

        Log.Info($"профиль пересобран из дневника: {count} фраз", "learn");
        return profile;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            try { _writer?.Dispose(); } catch { }
            _writer = null;
        }
    }
}

/// <summary>Одна услышанная фраза со всем, что о ней известно.</summary>
public sealed class JournalEntry
{
    public DateTime At { get; set; } = DateTime.UtcNow;

    /// <summary>Что распознали.</summary>
    public string Text { get; set; } = "";

    /// <summary>К нам ли обращались и насколько уверенно.</summary>
    public double WakeScore { get; set; }

    /// <summary>Что из этого поняли.</summary>
    public string Intent { get; set; } = "";

    /// <summary>Получилось ли что-то сделать.</summary>
    public bool Success { get; set; }

    /// <summary>Что в итоге запустили.</summary>
    public string EntryId { get; set; } = "";

    /// <summary>Сколько миллисекунд заняло распознавание.</summary>
    public int RecognitionMs { get; set; }
}
