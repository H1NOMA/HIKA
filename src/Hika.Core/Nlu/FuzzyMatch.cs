namespace Hika.Nlu;

/// <summary>
/// Нечёткое сравнение строк. Распознаватель речи ошибается в одну-две буквы
/// почти всегда, поэтому точное сравнение здесь бесполезно.
/// </summary>
public static class FuzzyMatch
{
    /// <summary>
    /// Расстояние Дамерау — Левенштейна: вставки, удаления, замены и перестановки
    /// соседних букв. Перестановки нужны отдельно, потому что распознаватель
    /// регулярно меняет соседние буквы местами.
    /// </summary>
    public static int Distance(string a, string b, int max = int.MaxValue)
    {
        if (a == b) return 0;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        // Разница в длине — нижняя граница расстояния, дальше можно не считать.
        if (Math.Abs(a.Length - b.Length) > max) return max + 1;

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        var beforePrevious = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++) previous[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];

            for (int j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;

                var value = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);

                // Перестановка соседей
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    value = Math.Min(value, beforePrevious[j - 2] + cost);

                current[j] = value;
                if (value < rowMin) rowMin = value;
            }

            if (rowMin > max) return max + 1;

            (beforePrevious, previous, current) = (previous, current, beforePrevious);
        }

        return previous[b.Length];
    }

    /// <summary>Похожесть двух строк, 0..1.</summary>
    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0) return 1;
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1;

        var longest = Math.Max(a.Length, b.Length);
        var distance = Distance(a, b);
        var score = 1.0 - (double)distance / longest;

        // Совпадающее начало — сильный признак: люди редко ошибаются в первых буквах,
        // а распознаватель чаще портит окончания.
        var prefix = CommonPrefixLength(a, b);
        if (prefix >= 3) score += Math.Min(0.08, prefix * 0.015);

        return Math.Clamp(score, 0, 1);
    }

    /// <summary>Лучшая похожесть среди всех сочетаний написаний.</summary>
    public static double BestSimilarity(IReadOnlyList<string> keysA, IReadOnlyList<string> keysB)
    {
        double best = 0;
        for (int i = 0; i < keysA.Count; i++)
        {
            for (int j = 0; j < keysB.Count; j++)
            {
                var score = Similarity(keysA[i], keysB[j]);
                if (score > best) best = score;
                if (best >= 0.999) return 1;
            }
        }
        return best;
    }

    public static double BestSimilarity(string a, string b)
        => BestSimilarity(Translit.Keys(a), Translit.Keys(b));

    private static int CommonPrefixLength(string a, string b)
    {
        var n = Math.Min(a.Length, b.Length);
        int i = 0;
        while (i < n && a[i] == b[i]) i++;
        return i;
    }

    /// <summary>
    /// Похожесть фраз из нескольких слов. Считается по словам, а не по строке
    /// целиком: «гугл хром» и «хром» должны совпадать хорошо, хотя строки
    /// различаются вдвое.
    /// </summary>
    public static double PhraseSimilarity(string[] spoken, string[] candidate)
        => PhraseSimilarity(
            spoken.Select(Translit.Keys).ToArray(),
            candidate.Select(Translit.Keys).ToArray());

    /// <summary>
    /// То же самое, но на заранее посчитанных написаниях. Каталог сопоставляется
    /// целиком на каждой команде, так что пересчитывать транслитерацию тысяч
    /// записей заново — заметная и совершенно лишняя работа.
    /// </summary>
    public static double PhraseSimilarity(string[][] spokenKeys, string[][] candidateKeys)
    {
        if (spokenKeys.Length == 0 || candidateKeys.Length == 0) return 0;

        // Каждое слово запроса ищет себе лучшую пару среди слов кандидата.
        double sum = 0;
        foreach (var keys in spokenKeys)
        {
            double best = 0;
            foreach (var candidate in candidateKeys)
            {
                var score = BestSimilarity(keys, candidate);
                if (score > best) best = score;
                if (best >= 0.999) break;
            }
            sum += best;
        }

        var coverage = sum / spokenKeys.Length;

        // Лишние слова у кандидата слегка снижают оценку: при прочих равных
        // «Chrome» должен обойти «Chrome Remote Desktop» на запрос «хром».
        var extra = Math.Max(0, candidateKeys.Length - spokenKeys.Length);
        var penalty = Math.Min(0.25, extra * 0.07);

        return Math.Clamp(coverage - penalty, 0, 1);
    }
}
