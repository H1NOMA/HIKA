using Hika.Config;
using Xunit;

namespace Hika.Tests;

/// <summary>
/// Разбор записи горячей клавиши.
///
/// Проверяется здесь то единственное, что можно проверить без Windows:
/// во что превращается строка. Само назначение клавиши системе тестами
/// не покрывается никак — зато, зная разбор, «почему Ctrl+Alt+Пробел
/// не работает» выясняется за минуту вместо вечера.
/// </summary>
public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space", 0x0002 | 0x0001, 0x20)]
    [InlineData("ctrl+alt+space", 0x0002 | 0x0001, 0x20)]
    [InlineData("Ctrl + Alt + Space", 0x0002 | 0x0001, 0x20)]
    [InlineData("Ctrl+Alt+H", 0x0002 | 0x0001, 'H')]
    [InlineData("Shift+F5", 0x0004, 0x74)]
    [InlineData("Win+K", 0x0008, 'K')]
    [InlineData("Ctrl+Shift+1", 0x0002 | 0x0004, '1')]
    [InlineData("Alt+Enter", 0x0001, 0x0D)]
    [InlineData("Ctrl+PageUp", 0x0002, 0x21)]
    [InlineData("F9", 0, 0x78)]
    public void СочетаниеРазбирается(string text, uint modifiers, uint key)
    {
        var hotkey = Hotkey.Parse(text);

        Assert.NotNull(hotkey);
        Assert.Equal(modifiers, hotkey!.Modifiers);
        Assert.Equal(key, hotkey.Key);
    }

    [Theory]
    // Как бы ни записал человек, в настройках и в журнале сочетание выглядит
    // одинаково — иначе одну и ту же клавишу не узнать в двух местах.
    [InlineData("Ctrl+Alt+Space", "Ctrl+Alt+Space")]
    [InlineData("alt+ctrl+space", "Ctrl+Alt+Space")]
    [InlineData("shift+f5", "Shift+F5")]
    [InlineData("CTRL+h", "Ctrl+H")]
    [InlineData("ctrl+alt+пробел", "Ctrl+Alt+Space")]
    public void ЗаписьПриводитсяКОдномуВиду(string text, string expected)
    {
        Assert.Equal(expected, Hotkey.Parse(text)!.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Alt+Пробелище")]
    [InlineData("Ctrl+A+B")]
    public void НепонятноеНеРазбирается(string? text)
    {
        Assert.Null(Hotkey.Parse(text));
    }

    [Theory]
    // Голая буква в глобальные не годится: назначить «H» на всю систему
    // значит, что эта буква перестанет набираться где бы то ни было.
    [InlineData("H")]
    [InlineData("Space")]
    [InlineData("Enter")]
    [InlineData("1")]
    public void КлавишаБезМодификатораНеПринимается(string text)
    {
        Assert.Null(Hotkey.Parse(text));
    }

    [Theory]
    // Кроме тех, что сами по себе ничего не печатают.
    [InlineData("F1")]
    [InlineData("F12")]
    [InlineData("Pause")]
    public void ФункциональнаяКлавишаМожетБытьОдна(string text)
    {
        Assert.NotNull(Hotkey.Parse(text));
    }

    [Fact]
    public void ЗначенияПоУмолчаниюРабочие()
    {
        // Если эти две записи перестанут разбираться, программа промолчит,
        // а человек будет жать клавишу и не понимать, почему ничего нет.
        var defaults = new BehaviorConfig();

        Assert.True(Hotkey.IsValid(defaults.ListenHotkey), $"«{defaults.ListenHotkey}» не разбирается");
        Assert.True(Hotkey.IsValid(defaults.MuteHotkey), $"«{defaults.MuteHotkey}» не разбирается");
    }
}
