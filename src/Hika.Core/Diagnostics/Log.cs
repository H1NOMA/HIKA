using System.Collections.Concurrent;
using System.Text;

namespace Hika.Diagnostics;

public enum LogLevel { Trace, Debug, Info, Warn, Error }

/// <summary>
/// Простой журнал с записью в файл из отдельного потока.
///
/// Разработка идёт не на Windows, поэтому журнал — основной (часто единственный)
/// способ понять, что произошло на машине пользователя. Пишем щедро,
/// но никогда не пишем распознанный текст на уровне Info: то, что человек
/// сказал вслух, не должно оседать в файле по умолчанию.
/// </summary>
public static class Log
{
    private static readonly BlockingCollection<string> Queue = new(new ConcurrentQueue<string>(), 4096);
    private static readonly object InitLock = new();
    private static Thread? _writer;
    private static string? _logPath;
    private static volatile bool _consoleEcho;

    // Где мы сейчас пишем и сколько уже написали. Нужно, чтобы вовремя
    // начать следующий файл: подробности — в LogRotation.
    private static DateTime _fileDay = DateTime.MinValue;
    private static int _part = 1;
    private static long _written;

    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    public static string LogDirectory { get; private set; } = "";

    public static void Initialize(string directory, bool consoleEcho = false)
    {
        lock (InitLock)
        {
            if (_writer is not null) { _consoleEcho = consoleEcho; return; }

            LogDirectory = directory;
            _consoleEcho = consoleEcho;

            try
            {
                Directory.CreateDirectory(directory);
                TrimOldLogs(directory);

                // Продолжаем ту часть, на которой остановились до перезапуска,
                // а не начинаем новую: иначе за день набралось бы столько
                // файлов, сколько раз человек перезагрузил компьютер.
                _fileDay = DateTime.Now.Date;
                _part = LogRotation.LastPart(Directory.GetFiles(directory, "hika-*.log"), _fileDay);
                _logPath = Path.Combine(directory, LogRotation.FileName(_fileDay, _part));
                _written = FileSize(_logPath);
            }
            catch
            {
                // Нет доступа к диску — работаем без файла, приложение важнее журнала.
                _logPath = null;
            }

            _writer = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "hika-log",
                Priority = ThreadPriority.BelowNormal,
            };
            _writer.Start();
        }
    }

    public static void Trace(string message, string? scope = null) => Write(LogLevel.Trace, scope, message);
    public static void Debug(string message, string? scope = null) => Write(LogLevel.Debug, scope, message);
    public static void Info(string message, string? scope = null) => Write(LogLevel.Info, scope, message);
    public static void Warn(string message, string? scope = null) => Write(LogLevel.Warn, scope, message);

    public static void Error(string message, string? scope = null) => Write(LogLevel.Error, scope, message);

    public static void Error(string message, Exception ex, string? scope = null)
        => Write(LogLevel.Error, scope, $"{message} :: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");

    private static void Write(LogLevel level, string? scope, string message)
    {
        if (level < MinimumLevel) return;

        var line = new StringBuilder(message.Length + 48)
            .Append(DateTime.Now.ToString("HH:mm:ss.fff"))
            .Append(' ')
            .Append(Tag(level))
            .Append(' ');

        if (!string.IsNullOrEmpty(scope)) line.Append('[').Append(scope).Append("] ");
        line.Append(message);

        var text = line.ToString();

        if (_consoleEcho)
        {
            try { Console.WriteLine(text); } catch { /* консоли может не быть */ }
        }

        // Журнал никогда не должен тормозить пайплайн: если очередь переполнена, теряем строку.
        Queue.TryAdd(text);
    }

    private static string Tag(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Info => "INF",
        LogLevel.Warn => "WRN",
        LogLevel.Error => "ERR",
        _ => "???",
    };

    private static void WriterLoop()
    {
        var buffer = new List<string>(64);

        foreach (var first in Queue.GetConsumingEnumerable())
        {
            buffer.Clear();
            buffer.Add(first);

            // Забираем всё, что накопилось, одной пачкой — меньше обращений к диску.
            while (buffer.Count < 256 && Queue.TryTake(out var more)) buffer.Add(more);

            if (_logPath is null) continue;

            try
            {
                RollIfNeeded();

                File.AppendAllLines(_logPath, buffer, Encoding.UTF8);

                // Считаем сами, а не спрашиваем длину файла: обращение к диску
                // на каждую пачку строк — ровно то, ради чего пачки и собираются.
                foreach (var line in buffer) _written += Encoding.UTF8.GetByteCount(line) + 2;
            }
            catch
            {
                // Файл занят или диск полон — молча продолжаем.
            }
        }
    }

    /// <summary>
    /// Начинает следующий файл, когда наступил новый день или текущий дорос
    /// до предела. Зовётся из потока записи и только оттуда.
    /// </summary>
    private static void RollIfNeeded()
    {
        var now = DateTime.Now;
        if (!LogRotation.ShouldRoll(_fileDay, now, _written)) return;

        var newDay = now.Date != _fileDay.Date;

        _part = newDay ? 1 : _part + 1;
        _fileDay = now.Date;
        _written = 0;
        _logPath = Path.Combine(LogDirectory, LogRotation.FileName(_fileDay, _part));

        // Новый день — повод и прибраться: программа, живущая в трее месяцами,
        // до сих пор делала уборку ровно один раз, при запуске.
        if (newDay) TrimOldLogs(LogDirectory);
    }

    private static long FileSize(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    /// <summary>Оставляем журналы за последнюю неделю, остальное удаляем.</summary>
    private static void TrimOldLogs(string directory)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-7);
            foreach (var file in Directory.GetFiles(directory, "hika-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
            }
        }
        catch { /* уборка не критична */ }
    }

    public static void Flush(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Queue.Count > 0 && DateTime.UtcNow < deadline) Thread.Sleep(10);
    }
}
