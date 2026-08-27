using System.Diagnostics;
using Hika.Diagnostics;
using Hika.Interop;
using Hika.Nlu;

namespace Hika.Skills;

/// <summary>
/// Переключение на уже открытое окно.
///
/// «Переключись на хром» и «открой хром» — разные просьбы, и разница
/// чувствуется сразу. Запуск открывает новое окно поверх той работы, которая
/// уже шла; переключение возвращает к ней. Человек, у которого браузер открыт
/// со вчерашнего дня, просит именно второго.
///
/// Сопоставление идёт по тем же правилам, что и весь остальной разбор команд:
/// заголовок окна и имя процесса сравниваются нечётко и через транслитерацию,
/// поэтому «переключись на хром» находит окно с заголовком «… — Google Chrome»,
/// а «на телегу» — Telegram.
/// </summary>
public static class WindowSwitcher
{
    private sealed record Candidate(IntPtr Handle, string Title, string Process, double Score);

    /// <summary>
    /// Окна, которые никогда не бывают целью. Переключиться на рабочий стол
    /// или на собственные настройки человек просит другими словами.
    /// </summary>
    private static readonly string[] Ignored =
    {
        "Program Manager", "Windows Input Experience", "Настройки HIKA", "HIKA — настройки",
    };

    /// <summary>
    /// Переключается на подходящее окно. Возвращает null, если ничего
    /// похожего не открыто — тогда вызывающий волен запустить программу.
    /// </summary>
    public static SkillResult? TryFocus(string phrase, double threshold = 0.55)
    {
        var best = FindBest(phrase, threshold);
        if (best is null) return null;

        try
        {
            // Свёрнутое окно поднимаем: SetForegroundWindow сам этого не делает,
            // и без восстановления команда выглядит как несработавшая.
            if (Win32.IsIconic(best.Handle)) Win32.ShowWindow(best.Handle, Win32.SW_RESTORE);

            if (!Win32.SetForegroundWindow(best.Handle))
            {
                // Windows не даёт поднять чужое окно программе, которая сейчас
                // не на переднем плане. Обходной путь — короткое мигание:
                // окно сначала показывается, потом поднимается.
                Win32.ShowWindow(best.Handle, Win32.SW_RESTORE);
                Win32.SetForegroundWindow(best.Handle);
            }

            var name = string.IsNullOrWhiteSpace(best.Process) ? best.Title : best.Process;
            Log.Info($"переключилась на «{Shorten(best.Title)}» ({best.Score:F2})", "system");

            return SkillResult.Ok($"переключилась на {Shorten(name)}");
        }
        catch (Exception ex)
        {
            Log.Error("переключение на окно не удалось", ex, "system");
            return null;
        }
    }

    /// <summary>Есть ли открытое окно, похожее на названное.</summary>
    public static bool HasWindow(string phrase, double threshold = 0.55)
        => FindBest(phrase, threshold) is not null;

    private static Candidate? FindBest(string phrase, double threshold)
    {
        var tokens = TextNormalizer.Tokenize(phrase);
        if (tokens.Length == 0) return null;

        var spoken = tokens.Select(Translit.Keys).ToArray();

        Candidate? best = null;

        // Имя процесса по его номеру. Process.GetProcessById — дорогое
        // обращение, а у одного браузера бывает два десятка окон с одним
        // и тем же номером: без этого словаря половина времени команды
        // «переключись на хром» уходила на то, чтобы двадцать раз узнать,
        // что хром называется хромом.
        var names = new Dictionary<uint, string>();

        try
        {
            Win32.EnumWindows((handle, _) =>
            {
                if (!Win32.IsWindowVisible(handle)) return true;

                var title = Win32.WindowTitle(handle);
                if (title.Length < 2) return true;
                if (Ignored.Any(i => title.Equals(i, StringComparison.OrdinalIgnoreCase))) return true;

                var process = ProcessName(handle, names);

                // Сравниваем и с заголовком, и с именем процесса. Заголовок
                // у браузера — название страницы, и «хром» в нём стоит в самом
                // конце, а то и вовсе отсутствует; имя процесса надёжнее.
                var score = Math.Max(Similarity(spoken, title), Similarity(spoken, process));

                if (score >= threshold && (best is null || score > best.Score))
                    best = new Candidate(handle, title, process, score);

                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Warn($"обход окон сорвался: {ex.Message}", "system");
            return null;
        }

        return best;
    }

    /// <summary>
    /// Насколько сказанное похоже на строку. Заголовок сравнивается не целиком,
    /// а по лучшему совпадающему куску: в «YouTube — Google Chrome» нужное
    /// слово стоит одно среди четырёх, и сравнение фразы целиком его утопит.
    /// </summary>
    private static double Similarity(string[][] spoken, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        var words = TextNormalizer.Tokenize(text);
        if (words.Length == 0) return 0;

        var keys = words.Select(Translit.Keys).ToArray();

        // Скользящее окно по длине сказанного.
        double best = 0;
        var width = Math.Min(spoken.Length, keys.Length);

        for (int start = 0; start + width <= keys.Length; start++)
        {
            var score = FuzzyMatch.PhraseSimilarity(spoken, keys[start..(start + width)]);
            if (score > best) best = score;
        }

        return best;
    }

    private static string ProcessName(IntPtr handle, Dictionary<uint, string> cache)
    {
        try
        {
            Win32.GetWindowThreadProcessId(handle, out var pid);
            if (pid == 0) return "";

            if (cache.TryGetValue(pid, out var known)) return known;

            string name;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                name = process.ProcessName;
            }
            catch
            {
                // Процесс успел закрыться между обходом и запросом — обычное
                // дело, и запоминаем мы это тоже: спрашивать снова незачем.
                name = "";
            }

            cache[pid] = name;
            return name;
        }
        catch
        {
            return "";
        }
    }

    private static string Shorten(string text)
    {
        text = text.Trim();
        if (text.Length <= 40) return text;

        // Заголовки браузеров кончаются названием программы — оно и нужно.
        var dash = text.LastIndexOf(" — ", StringComparison.Ordinal);
        if (dash > 0 && text.Length - dash < 40) return text[(dash + 3)..];

        return text[..37] + "…";
    }
}
