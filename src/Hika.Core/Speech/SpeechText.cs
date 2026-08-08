using System.Globalization;
using System.Text;

namespace Hika.Speech;

/// <summary>
/// Приведение написанного текста к произносимому.
///
/// Языковая модель пишет для глаз: звёздочки вокруг важного, дефисы списков,
/// решётки заголовков, ссылки, смайлики. Всё это синтезатор либо прочитает
/// вслух («звёздочка звёздочка Steam звёздочка звёздочка»), либо споткнётся.
/// Убрать разметку в самом запросе не выйдет — модель всё равно иногда
/// её ставит, — так что чистить приходится здесь.
///
/// Отдельно живёт разбиение на предложения. Ответ приходит потоком, и ждать
/// его конца, чтобы начать говорить, значит подарить человеку несколько секунд
/// тишины. Вместо этого первое же законченное предложение уходит в озвучку,
/// пока модель дописывает второе.
/// </summary>
public static class SpeechText
{
    /// <summary>Минимальная длина куска, который имеет смысл произносить отдельно.</summary>
    private const int MinChunk = 12;

    /// <summary>
    /// Готовит текст к произнесению: снимает разметку, символы и лишние пробелы.
    /// </summary>
    public static string ForSpeaking(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var sb = new StringBuilder(text.Length);
        var lineStart = true;

        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (ch is '\r') continue;

            if (ch is '\n')
            {
                // Перевод строки — это пауза, а не звук. Точка с пробелом
                // заставляет синтезатор сделать её сам.
                if (sb.Length > 0 && sb[^1] is not ('.' or '!' or '?' or ':')) sb.Append('.');
                sb.Append(' ');
                lineStart = true;
                continue;
            }

            // Разметка начала строки: заголовки, маркеры списков, цитаты.
            if (lineStart)
            {
                if (ch is ' ' or '\t') continue;
                if (ch is '#' or '>' or '|') continue;
                if ((ch is '-' or '*' or '+') && i + 1 < text.Length && text[i + 1] == ' ') { i++; continue; }
            }

            lineStart = false;

            switch (ch)
            {
                // Выделения и код — только для глаз.
                case '*':
                case '_':
                case '`':
                case '~':
                case '#':
                case '|':
                    continue;

                // Скобки ссылок читать вслух незачем.
                case '[':
                case ']':
                    continue;

                case '\t':
                    sb.Append(' ');
                    continue;
            }

            // Смайлики, стрелки, значки — всё, что не буква, не цифра,
            // не знак препинания и не пробел.
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category is UnicodeCategory.OtherSymbol or UnicodeCategory.Surrogate
                or UnicodeCategory.PrivateUse or UnicodeCategory.Format) continue;

            sb.Append(ch);
        }

        // Схлопываем пробелы, оставшиеся от вычищенного.
        var result = new StringBuilder(sb.Length);
        var space = false;

        foreach (var ch in sb.ToString())
        {
            if (ch == ' ')
            {
                if (!space && result.Length > 0) result.Append(' ');
                space = true;
                continue;
            }
            space = false;
            result.Append(ch);
        }

        var speakable = result.ToString().Trim();

        // Остались одни знаки препинания — произносить нечего.
        return speakable.Any(char.IsLetterOrDigit) ? speakable : "";
    }

    /// <summary>
    /// Отрезает от накопленного текста всё, что уже можно произнести,
    /// и оставляет в буфере незаконченный хвост.
    ///
    /// Кусок отдаётся, только если он достаточно длинный: «Да.» само по себе
    /// произносить отдельным вызовом синтеза дороже, чем дождаться следующей
    /// фразы и сказать их вместе.
    /// </summary>
    public static string? TakeSpeakable(StringBuilder buffer, bool flush = false)
    {
        if (buffer.Length == 0) return null;

        var text = buffer.ToString();

        if (flush)
        {
            buffer.Clear();
            var all = ForSpeaking(text);
            return all.Length > 0 ? all : null;
        }

        var cut = -1;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('.' or '!' or '?' or '…' or '\n')) continue;

            // Точка внутри числа или сокращения концом фразы не считается.
            if (text[i] == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1])) continue;

            // Конец предложения — только если дальше пробел или строка кончилась.
            if (i + 1 < text.Length && text[i + 1] is not (' ' or '\n' or '\r' or '"' or '»')) continue;

            // Именно первая подходящая граница, а не последняя. Смысл всей
            // затеи — начать говорить как можно раньше; отдав разом три
            // предложения, мы бы этот выигрыш и потеряли.
            if (i + 1 >= MinChunk) { cut = i + 1; break; }
        }

        if (cut < 0)
        {
            // Предложение затянулось — говорить всё равно пора, иначе человек
            // будет ждать конца абзаца. Режем по запятой.
            if (text.Length < 220) return null;

            cut = text.LastIndexOf(", ", StringComparison.Ordinal);
            if (cut < MinChunk) return null;
            cut += 1;
        }

        var chunk = text[..cut];
        buffer.Remove(0, cut);

        var spoken = ForSpeaking(chunk);
        return spoken.Length > 0 ? spoken : null;
    }

    /// <summary>Разбивает готовый текст на произносимые куски целиком.</summary>
    public static List<string> Split(string text)
    {
        var result = new List<string>();
        var buffer = new StringBuilder(text);

        while (true)
        {
            var chunk = TakeSpeakable(buffer);
            if (chunk is null) break;
            result.Add(chunk);
        }

        var tail = TakeSpeakable(buffer, flush: true);
        if (tail is not null) result.Add(tail);

        return result;
    }
}
