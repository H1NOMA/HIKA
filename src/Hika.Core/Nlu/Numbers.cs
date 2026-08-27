namespace Hika.Nlu;

/// <summary>
/// Числа, названные словами.
///
/// «Сделай громкость тридцать», «таймер на пять минут», «поставь двадцать
/// процентов» — распознаватель отдаёт это словами, а не цифрами, и без разбора
/// такие команды не работают вовсе.
///
/// Разбор устроен как счёт вслух: числительные складываются, пока идут по
/// убыванию разряда. «Двадцать пять» — двадцать плюс пять; «сто двадцать
/// пять» — сто плюс двадцать плюс пять. Как только разряд перестаёт убывать,
/// число закончилось: во «включи два, три» это два разных числа, а не
/// двадцать три.
/// </summary>
public static class Numbers
{
    private static readonly Dictionary<string, int> Words = new(StringComparer.Ordinal)
    {
        ["ноль"] = 0, ["нуль"] = 0,
        ["один"] = 1, ["одна"] = 1, ["одну"] = 1, ["первый"] = 1, ["раз"] = 1,
        ["два"] = 2, ["две"] = 2, ["второй"] = 2, ["пару"] = 2, ["пара"] = 2,
        ["три"] = 3, ["третий"] = 3,
        ["четыре"] = 4, ["четвертый"] = 4, ["четвёртый"] = 4,
        ["пять"] = 5, ["пятый"] = 5,
        ["шесть"] = 6, ["шестой"] = 6,
        ["семь"] = 7, ["седьмой"] = 7,
        ["восемь"] = 8, ["восьмой"] = 8,
        ["девять"] = 9, ["девятый"] = 9,
        ["десять"] = 10, ["десятый"] = 10,
        ["одиннадцать"] = 11, ["двенадцать"] = 12, ["тринадцать"] = 13,
        ["четырнадцать"] = 14, ["пятнадцать"] = 15, ["шестнадцать"] = 16,
        ["семнадцать"] = 17, ["восемнадцать"] = 18, ["девятнадцать"] = 19,
        ["двадцать"] = 20, ["тридцать"] = 30, ["сорок"] = 40, ["полсотни"] = 50,
        ["пятьдесят"] = 50, ["шестьдесят"] = 60, ["семьдесят"] = 70,
        ["восемьдесят"] = 80, ["девяносто"] = 90, ["сто"] = 100,

        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4,
        ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9,
        ["ten"] = 10, ["fifteen"] = 15, ["twenty"] = 20, ["thirty"] = 30,
        ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70,
        ["eighty"] = 80, ["ninety"] = 90, ["hundred"] = 100,
    };

    /// <summary>Слова, которые сами по себе означают половину предыдущей единицы.</summary>
    private static readonly HashSet<string> Halves = new(StringComparer.Ordinal)
    {
        "полминуты", "полчаса", "полминутки",
    };

    /// <summary>Слово — числительное или цифра.</summary>
    public static bool IsNumber(string token)
        => Words.ContainsKey(token) || (token.Length > 0 && token.All(char.IsDigit));

    /// <summary>
    /// Читает число с указанной позиции. Возвращает null, если числа там нет.
    /// </summary>
    /// <param name="consumed">Сколько слов заняло число.</param>
    public static int? Read(string[] tokens, int start, out int consumed)
    {
        consumed = 0;
        if (start < 0 || start >= tokens.Length) return null;

        // Цифрами — коротко и однозначно.
        if (int.TryParse(tokens[start], out var digits))
        {
            consumed = 1;
            return digits;
        }

        if (!Words.TryGetValue(tokens[start], out var total)) return null;

        consumed = 1;
        var previous = total;

        // Складываем, пока разряд убывает: «сто двадцать пять».
        for (int i = start + 1; i < tokens.Length; i++)
        {
            if (!Words.TryGetValue(tokens[i], out var next)) break;
            if (next >= previous) break;

            total += next;
            previous = next;
            consumed++;
        }

        return total;
    }

    /// <summary>Первое число во фразе. Null, если чисел нет.</summary>
    public static int? First(string[] tokens)
    {
        for (int i = 0; i < tokens.Length; i++)
        {
            var value = Read(tokens, i, out _);
            if (value is not null) return value;
        }
        return null;
    }

    /// <summary>
    /// Промежуток времени из фразы вроде «пять минут», «полчаса», «на десять секунд».
    /// Null, если промежутка не видно.
    /// </summary>
    public static TimeSpan? Duration(string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (!Halves.Contains(token)) continue;
            return token == "полчаса" ? TimeSpan.FromMinutes(30) : TimeSpan.FromSeconds(30);
        }

        for (int i = 0; i < tokens.Length; i++)
        {
            var value = Read(tokens, i, out var consumed);
            if (value is null || value <= 0) continue;

            // Единица измерения стоит сразу после числа. Её отсутствие
            // означает минуты: «поставь таймер на пять» — это пять минут,
            // а не пять секунд и не пять часов.
            var unit = i + consumed < tokens.Length ? tokens[i + consumed] : "";

            if (IsSeconds(unit)) return TimeSpan.FromSeconds(value.Value);
            if (IsHours(unit)) return TimeSpan.FromHours(value.Value);

            return TimeSpan.FromMinutes(value.Value);
        }

        // Числа нет вовсе — но «через час» и «напомни через минуту» люди
        // говорят чаще, чем «через один час». Единица без числа означает одну.
        foreach (var token in tokens)
        {
            if (IsHours(token)) return TimeSpan.FromHours(1);
            if (IsMinutes(token)) return TimeSpan.FromMinutes(1);
            if (IsSeconds(token)) return TimeSpan.FromSeconds(1);
        }

        return null;
    }

    private static bool IsSeconds(string unit)
        => unit is "секунду" or "секунды" or "секунд" or "секунда" or "сек" or "second" or "seconds";

    private static bool IsHours(string unit)
        => unit is "час" or "часа" or "часов" or "часу" or "hour" or "hours";

    private static bool IsMinutes(string unit)
        => unit is "минуту" or "минута" or "минуты" or "минут" or "минутку" or "мин"
                or "minute" or "minutes";
}
