using System.Text.Json.Serialization;

namespace Hika.Learning;

/// <summary>
/// Всё, что HIKA узнала лично про этого человека.
///
/// Сразу о том, чем это не является. Дообучить нейросеть распознавания речи
/// на домашнем компьютере нельзя: это недели вычислений на видеокартах, которых
/// у нас нет, и гигабайты чужой речи для сравнения, которых у нас тоже нет.
/// Обещать «обучу модель на твоём голосе» было бы враньём.
///
/// Зато можно другое, и на слух разница выходит примерно та же. Распознавание
/// подсказывается словарём: whisper заметно охотнее слышит слова, которые ему
/// показали заранее. Значит, чем дольше человек пользуется программой, тем
/// точнее становится этот словарь — и тем реже «халдайверс» превращается
/// в «хал драйвер». Плюс запоминаются собственные ошибки: если команда
/// не нашлась, а следом почти та же нашлась, связь между ними сохраняется
/// навсегда.
///
/// Всё это лежит в одном файле в %APPDATA%\HIKA\profile.json, никуда не уходит
/// и стирается его удалением.
/// </summary>
public sealed class UserProfile
{
    public int Version { get; set; } = 1;

    /// <summary>Когда начали наблюдать. Нужно только чтобы показать человеку срок.</summary>
    public DateTime Since { get; set; } = DateTime.UtcNow;

    public long Utterances { get; set; }
    public long Commands { get; set; }
    public long Successes { get; set; }

    /// <summary>Слова, которые человек реально произносит, и как часто.</summary>
    public Dictionary<string, TermStat> Terms { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Выученные синонимы: как человек сказал -> что в итоге оказалось нужно.
    /// Растут сами из неудач, за которыми сразу шла удача.
    /// </summary>
    public Dictionary<string, AliasStat> Aliases { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Как распознавание на самом деле пишет имя ассистента. «Хика» вполне может
    /// приезжать «хикой», «фикой» и «икеа» — и если что-то повторяется, значит,
    /// человек так говорит, а не оговорился.
    /// </summary>
    public Dictionary<string, int> WakeVariants { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Что и сколько раз запускали. Отсюда берётся приоритет при равных оценках.</summary>
    public Dictionary<string, int> Launches { get; set; } = new(StringComparer.Ordinal);

    [JsonIgnore]
    public double SuccessRate => Commands == 0 ? 0 : (double)Successes / Commands;
}

/// <summary>Слово и то, насколько оно своё.</summary>
public sealed class TermStat
{
    public int Count { get; set; }
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Слово приводило к успешно выполненной команде. Такие идут в словарь
    /// распознавания первыми: они точно что-то значат, а не послышались.
    /// </summary>
    public int Useful { get; set; }
}

/// <summary>Выученный синоним.</summary>
public sealed class AliasStat
{
    /// <summary>Идентификатор записи каталога, в которую всё вылилось.</summary>
    public string EntryId { get; set; } = "";

    /// <summary>Как эта запись называется по-человечески — для окна настроек.</summary>
    public string EntryName { get; set; } = "";

    public int Count { get; set; }
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    /// <summary>Синоним добавлен человеком руками, а не выведен из поведения.</summary>
    public bool Manual { get; set; }
}
