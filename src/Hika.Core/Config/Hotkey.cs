namespace Hika.Config;

/// <summary>
/// Сочетание клавиш, записанное строкой: «Ctrl+Alt+Space».
///
/// Живёт здесь, а не рядом с Windows API, по одной причине: разбор строки —
/// это то место, где ошибаются, и его надо проверять тестами. Само
/// назначение клавиши системе никакими тестами не покрывается, а вот
/// «почему Ctrl+Alt+Пробел не работает» разбирается за минуту, если известно,
/// во что превратилась строка.
/// </summary>
public sealed record Hotkey(uint Modifiers, uint Key, string Text)
{
    // Значения из RegisterHotKey. Продублированы здесь, чтобы ядро
    // не зависело от Windows.
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    /// <summary>Не повторять, пока клавишу держат: одно нажатие — одно срабатывание.</summary>
    public const uint ModNoRepeat = 0x4000;

    /// <summary>
    /// Разбирает запись сочетания. Возвращает null, если разобрать нечего
    /// или сочетание не годится в глобальные.
    ///
    /// Не годится — это про клавишу без модификатора. Назначить глобально
    /// одну «F» значит отнять эту букву у всей системы: она перестанет
    /// набираться где бы то ни было. Исключение — F1–F24 и Pause, которые
    /// сами по себе ничего не печатают.
    /// </summary>
    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        uint modifiers = 0;
        uint key = 0;
        var keyName = "";

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = raw.ToLowerInvariant();

            switch (part)
            {
                case "ctrl" or "control" or "ктрл":
                    modifiers |= ModControl;
                    continue;
                case "alt" or "меню":
                    modifiers |= ModAlt;
                    continue;
                case "shift" or "шифт":
                    modifiers |= ModShift;
                    continue;
                case "win" or "windows" or "cmd" or "вин":
                    modifiers |= ModWin;
                    continue;
            }

            // Основная клавиша может быть только одна. Вторая означает,
            // что запись испорчена, и угадывать здесь нечего.
            if (key != 0) return null;

            var code = KeyCode(part);
            if (code == 0) return null;

            key = code;
            keyName = Display(part);
        }

        if (key == 0) return null;

        // Клавиша без модификатора — только та, что сама по себе не печатается.
        if (modifiers == 0 && !IsSafeAlone(key)) return null;

        return new Hotkey(modifiers, key, Format(modifiers, keyName));
    }

    /// <summary>Годится ли сочетание — короткая проверка для окна настроек.</summary>
    public static bool IsValid(string? text) => Parse(text) is not null;

    private static bool IsSafeAlone(uint key)
        => key is >= 0x70 and <= 0x87   // F1..F24
        || key == 0x13                  // Pause
        || key == 0x91;                 // Scroll Lock

    private static string Format(uint modifiers, string keyName)
    {
        var parts = new List<string>(4);

        if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModShift) != 0) parts.Add("Shift");
        if ((modifiers & ModWin) != 0) parts.Add("Win");

        parts.Add(keyName);
        return string.Join("+", parts);
    }

    /// <summary>Как показать клавишу человеку.</summary>
    private static string Display(string part) => part switch
    {
        "space" or "пробел" => "Space",
        "enter" or "return" or "ввод" => "Enter",
        "escape" or "esc" => "Escape",
        "backspace" or "back" => "Backspace",
        "delete" or "del" => "Delete",
        "insert" or "ins" => "Insert",
        "pageup" or "prior" => "PageUp",
        "pagedown" or "next" => "PageDown",
        "tilde" or "oem3" => "Tilde",
        _ => part.Length == 1 ? part.ToUpperInvariant() : char.ToUpperInvariant(part[0]) + part[1..],
    };

    /// <summary>
    /// Код клавиши по её названию. Ноль — названия не знаем.
    ///
    /// Набор нарочно ограничен тем, что человек действительно назначает.
    /// Полная таблица виртуальных кодов Windows содержит две сотни записей,
    /// из которых сто девяносто не встречаются в горячих клавишах никогда.
    /// </summary>
    private static uint KeyCode(string name)
    {
        if (name.Length == 1)
        {
            var ch = char.ToUpperInvariant(name[0]);
            if (ch is >= 'A' and <= 'Z') return ch;
            if (ch is >= '0' and <= '9') return ch;
            return 0;
        }

        // F1..F24
        if (name[0] == 'f' && int.TryParse(name[1..], out var index) && index is >= 1 and <= 24)
            return (uint)(0x70 + index - 1);

        // Numpad0..Numpad9
        if (name.StartsWith("numpad", StringComparison.Ordinal)
            && int.TryParse(name[6..], out var digit) && digit is >= 0 and <= 9)
            return (uint)(0x60 + digit);

        return name switch
        {
            "space" or "пробел" => 0x20,
            "enter" or "return" or "ввод" => 0x0D,
            "tab" or "таб" => 0x09,
            "escape" or "esc" => 0x1B,
            "backspace" or "back" => 0x08,
            "delete" or "del" => 0x2E,
            "insert" or "ins" => 0x2D,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "prior" => 0x21,
            "pagedown" or "next" => 0x22,
            "up" => 0x26,
            "down" => 0x28,
            "left" => 0x25,
            "right" => 0x27,
            "pause" => 0x13,
            "scrolllock" => 0x91,
            "tilde" or "oem3" => 0xC0,
            "add" or "plus" => 0x6B,
            "subtract" or "minus" => 0x6D,
            "multiply" => 0x6A,
            "divide" => 0x6F,
            _ => 0,
        };
    }
}
