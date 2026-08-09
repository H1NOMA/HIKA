using Hika.Diagnostics;

namespace Hika.Skills;

/// <summary>
/// Таймеры и напоминания.
///
/// Живут в памяти и умирают вместе с программой — и это осознанное решение,
/// а не упрощение. Таймер, переживший перезагрузку и сработавший через сутки,
/// пугает: человек давно забыл, о чём просил. Всё, что дольше сегодняшнего
/// дня, называется другим словом и делается в календаре.
/// </summary>
public sealed class Timers : IDisposable
{
    private readonly List<System.Threading.Timer> _running = new();
    private readonly object _lock = new();

    /// <summary>Таймер сработал. Сюда подключается и голос, и всплывающее уведомление.</summary>
    public event Action<string>? Fired;

    public int Count { get { lock (_lock) return _running.Count; } }

    public SkillResult Start(TimeSpan delay)
    {
        if (delay < TimeSpan.FromSeconds(5)) return SkillResult.Fail("слишком короткий срок");
        if (delay > TimeSpan.FromHours(24)) return SkillResult.Fail("слишком долгий срок");

        try
        {
            System.Threading.Timer? timer = null;

            timer = new System.Threading.Timer(_ =>
            {
                lock (_lock)
                {
                    if (timer is not null) _running.Remove(timer);
                }

                try { timer?.Dispose(); } catch { }

                var message = $"Таймер на {Describe(delay)} — время вышло.";
                Log.Info(message, "timer");

                try { Fired?.Invoke(message); }
                catch (Exception ex) { Log.Error("обработчик таймера упал", ex, "timer"); }
            }, null, delay, System.Threading.Timeout.InfiniteTimeSpan);

            lock (_lock) _running.Add(timer);

            var description = $"таймер на {Describe(delay)}";
            Log.Info(description, "timer");
            return SkillResult.Ok(description);
        }
        catch (Exception ex)
        {
            Log.Error("таймер не завёлся", ex, "timer");
            return SkillResult.Fail("таймер не завёлся");
        }
    }

    /// <summary>Отменяет все идущие таймеры.</summary>
    public SkillResult CancelAll()
    {
        List<System.Threading.Timer> doomed;
        lock (_lock)
        {
            doomed = new List<System.Threading.Timer>(_running);
            _running.Clear();
        }

        foreach (var timer in doomed)
        {
            try { timer.Dispose(); } catch { }
        }

        return doomed.Count == 0
            ? SkillResult.Ok("таймеров не было")
            : SkillResult.Ok($"отменила таймеров: {doomed.Count}");
    }

    /// <summary>Срок словами, как его произнесли бы вслух.</summary>
    public static string Describe(TimeSpan delay)
    {
        if (delay.TotalSeconds < 60) return $"{(int)delay.TotalSeconds} секунд";
        if (delay.TotalMinutes < 60)
        {
            var minutes = (int)Math.Round(delay.TotalMinutes);
            return $"{minutes} {Plural(minutes, "минуту", "минуты", "минут")}";
        }

        var hours = (int)delay.TotalHours;
        var rest = (int)Math.Round(delay.TotalMinutes) - hours * 60;

        var text = $"{hours} {Plural(hours, "час", "часа", "часов")}";
        return rest > 0 ? $"{text} {rest} {Plural(rest, "минуту", "минуты", "минут")}" : text;
    }

    private static string Plural(int n, string one, string few, string many)
    {
        var last = n % 10;
        var teen = n % 100;

        if (teen is >= 11 and <= 14) return many;
        if (last == 1) return one;
        if (last is >= 2 and <= 4) return few;
        return many;
    }

    public void Dispose() => CancelAll();
}
