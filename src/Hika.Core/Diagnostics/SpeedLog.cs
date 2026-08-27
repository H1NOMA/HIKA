namespace Hika.Diagnostics;

/// <summary>
/// Из чего сложилось ожидание в одной команде.
///
/// Все четыре числа существуют по одной причине: «медленно» — не диагноз.
/// Полторы секунды ожидания могут быть полутора секундами распознавания,
/// а могут — четырьмя сотнями миллисекунд ожидания конца фразы плюс тем же
/// распознаванием, и лечится это в двух разных местах. Пока слагаемые
/// не разделены, любая попытка ускорить программу — угадывание.
/// </summary>
public readonly record struct SpeedSample
{
    /// <summary>От начала речи до вспышки каймы. Ноль — имя в этой фразе не проверялось.</summary>
    public int WakeMs { get; init; }

    /// <summary>Сколько ждали тишины, чтобы счесть фразу законченной.</summary>
    public int SilenceMs { get; init; }

    /// <summary>Распознавание фразы целиком.</summary>
    public int RecognitionMs { get; init; }

    /// <summary>Разбор команды и её исполнение.</summary>
    public int ActionMs { get; init; }

    /// <summary>Длина самой фразы — нужна, чтобы понять, успевает ли модель за речью.</summary>
    public int AudioMs { get; init; }

    /// <summary>Всё, что человек прождал после того, как договорил.</summary>
    public int TotalMs => SilenceMs + RecognitionMs + ActionMs;

    /// <summary>
    /// Во сколько раз распознавание медленнее речи.
    ///
    /// Единица — модель считает ровно столько, сколько длилась фраза. Всё,
    /// что больше, означает, что она не успевает, и с ростом длины фразы
    /// ожидание будет расти вместе с ней.
    /// </summary>
    public double RealTime => AudioMs <= 0 ? 0 : RecognitionMs / (double)AudioMs;
}

/// <summary>Медианы по последним командам.</summary>
public readonly record struct SpeedSummary
{
    public int Commands { get; init; }
    public int WakeMs { get; init; }
    public int SilenceMs { get; init; }
    public int RecognitionMs { get; init; }
    public int ActionMs { get; init; }
    public double RealTime { get; init; }

    public int TotalMs => SilenceMs + RecognitionMs + ActionMs;
}

/// <summary>
/// Последние команды и то, сколько они заняли.
///
/// Считает медиану, а не среднее, и это существенно: одна команда, попавшая
/// на переиндексацию программ или на загрузку модели, займёт восемь секунд
/// и утащит среднее туда, где человек никогда не был. Медиана показывает
/// обычный день.
///
/// Живёт в памяти и умирает вместе с программой. На диск это не пишется:
/// сведения нужны, только пока человек смотрит на них в окне настроек.
/// </summary>
public sealed class SpeedLog
{
    /// <summary>Сколько команд помнить. Двух десятков хватает, чтобы медиана перестала прыгать.</summary>
    private const int Keep = 24;

    private readonly object _lock = new();
    private readonly Queue<SpeedSample> _samples = new();

    private SpeedSample? _last;

    public void Add(SpeedSample sample)
    {
        lock (_lock)
        {
            _last = sample;
            _samples.Enqueue(sample);

            while (_samples.Count > Keep) _samples.Dequeue();
        }
    }

    /// <summary>Последняя команда. Null — ещё ни одной не было.</summary>
    public SpeedSample? Last
    {
        get { lock (_lock) return _last; }
    }

    public int Count
    {
        get { lock (_lock) return _samples.Count; }
    }

    /// <summary>Медианы. Null — измерять ещё нечего.</summary>
    public SpeedSummary? Summary()
    {
        SpeedSample[] samples;
        lock (_lock) samples = _samples.ToArray();

        if (samples.Length == 0) return null;

        return new SpeedSummary
        {
            Commands = samples.Length,

            // Пробуждение считается только по тем командам, где имя вообще
            // проверялось: команда, сказанная в окне продолжения, имени
            // не содержит, и ноль от неё занизил бы медиану вдвое.
            WakeMs = Median(samples.Where(s => s.WakeMs > 0).Select(s => (double)s.WakeMs)),

            SilenceMs = Median(samples.Select(s => (double)s.SilenceMs)),
            RecognitionMs = Median(samples.Select(s => (double)s.RecognitionMs)),
            ActionMs = Median(samples.Select(s => (double)s.ActionMs)),
            RealTime = MedianExact(samples.Where(s => s.AudioMs > 0).Select(s => s.RealTime)),
        };
    }

    public void Clear()
    {
        lock (_lock)
        {
            _samples.Clear();
            _last = null;
        }
    }

    private static int Median(IEnumerable<double> values) => (int)Math.Round(MedianExact(values));

    private static double MedianExact(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0) return 0;

        var middle = sorted.Length / 2;

        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
