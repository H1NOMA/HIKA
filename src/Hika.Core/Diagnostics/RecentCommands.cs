namespace Hika.Diagnostics;

/// <summary>Чем закончилась услышанная фраза.</summary>
public enum HeardOutcome
{
    /// <summary>Обращались не к нам — имя не прозвучало.</summary>
    NotForUs,

    /// <summary>Имя услышано, а команду разобрать не вышло.</summary>
    NotUnderstood,

    /// <summary>Команда разобрана и выполнена.</summary>
    Done,

    /// <summary>Команда разобрана, но исполнить не удалось.</summary>
    Failed,

    /// <summary>Ушло в разговор.</summary>
    Talk,
}

/// <summary>Одна услышанная фраза со всем, что о ней известно.</summary>
public sealed record Heard(
    string Text,
    string Intent,
    string Result,
    HeardOutcome Outcome,
    int TotalMs,
    double WakeScore);

/// <summary>
/// Последние услышанные фразы — и что с каждой стало.
///
/// Единственный ответ на вопрос «почему она открыла не то», который человек
/// может получить сам. Причина такой ошибки всегда одна из трёх: расслышала
/// не то, разобрала не так, нашла не ту программу, — и все три видны только
/// рядом. Порознь они выглядят как «программа сошла с ума».
///
/// До сих пор это существовало в двух местах, одинаково недоступных: в журнале,
/// который человек не читает, и в отдельном запуске из консоли, который он
/// не откроет. Здесь то же самое лежит в окне настроек.
///
/// Только в памяти и только последние несколько фраз: это чужая речь, и место
/// ей на диске — там, где человек сам разрешил её хранить.
/// </summary>
public sealed class RecentCommands
{
    private const int Keep = 12;

    private readonly object _lock = new();
    private readonly Queue<Heard> _items = new();

    public void Add(Heard item)
    {
        lock (_lock)
        {
            _items.Enqueue(item);
            while (_items.Count > Keep) _items.Dequeue();
        }
    }

    /// <summary>Последние сверху.</summary>
    public IReadOnlyList<Heard> Items()
    {
        lock (_lock) return _items.Reverse().ToArray();
    }

    public void Clear()
    {
        lock (_lock) _items.Clear();
    }

    /// <summary>
    /// Общий вывод по последним фразам — то, ради чего человек сюда и пришёл.
    ///
    /// Три разных беды выглядят снаружи одинаково («не работает»), а лечатся
    /// в трёх разных местах: имя не узнаётся — правится словарём произношений;
    /// команда не разбирается — формулировкой или своей командой; программа
    /// не находится — порогом уверенности. Назвать, какая из трёх, может
    /// только тот, кто видит все фразы разом.
    /// </summary>
    public string Verdict()
    {
        var items = Items();
        if (items.Count < 3) return "";

        var forUs = items.Count(i => i.Outcome != HeardOutcome.NotForUs);
        if (forUs == 0)
            return "Ни одна фраза не признана обращением. Похоже, имя расслышивается как-то иначе — " +
                   "посмотрите, как именно, в списке ниже и впишите это написание в «Личность» → " +
                   "«Свои варианты произношения».";

        var understood = items.Count(i => i.Outcome is HeardOutcome.Done or HeardOutcome.Talk);
        var failed = items.Count(i => i.Outcome == HeardOutcome.Failed);
        var unknown = items.Count(i => i.Outcome == HeardOutcome.NotUnderstood);

        if (unknown > understood && unknown >= 2)
            return "Имя узнаётся, а команды — нет. Обычно это значит, что распознаватель коверкает " +
                   "слова: посмотрите в списке, что именно он услышал. Помогает более крупная модель " +
                   "или своя команда в разделе «Поведение».";

        if (failed > understood && failed >= 2)
            return "Команды разбираются, но не выполняются. Чаще всего это ненайденная программа: " +
                   "опустите «Уверенность при поиске программы» в разделе «Поведение» или добавьте " +
                   "свою команду.";

        return "";
    }
}
