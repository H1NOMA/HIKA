namespace Hika.Skills;

/// <summary>
/// Выполняет действие в потоке с однопоточной моделью COM.
///
/// Оболочка Windows (Shell.Application) в других моделях работает через маршалинг
/// и время от времени просто отказывает. Команды приходят из рабочего потока
/// пайплайна, поэтому COM-вызовы уводим сюда.
/// </summary>
internal static class StaRunner
{
    public static T? Run<T>(Func<T?> action, int timeoutMs = 10000)
    {
        T? result = default;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { failure = ex; }
        })
        {
            IsBackground = true,
            Name = "hika-sta",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(timeoutMs)) throw new TimeoutException("вызов оболочки Windows не завершился вовремя");
        if (failure is not null) throw failure;

        return result;
    }

    public static void Run(Action action, int timeoutMs = 10000)
        => Run<object?>(() => { action(); return null; }, timeoutMs);
}
