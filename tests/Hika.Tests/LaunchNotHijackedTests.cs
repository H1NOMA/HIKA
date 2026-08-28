using Hika.Nlu;
using Xunit;

namespace Hika.Tests;

/// <summary>
/// Запуск программы не перехватывается командой.
///
/// Оценка шаблона — среднее по слотам, и это её слабое место: точно совпавший
/// глагол вытягивает совсем не тот предмет. «Включи мозиллу» разбиралось как
/// «включи музыку», «открой переводчик» — как «открой проводник», «включи
/// заметки» — как «замедли». Единица за глагол и семь десятых за предмет дают
/// в среднем достаточно, хотя предмет — другое слово.
///
/// Все фразы здесь — настоящие имена из встроенного каталога программ,
/// и каждая когда-то ломалась.
/// </summary>
public class LaunchNotHijackedTests
{
    [Theory]
    [InlineData("включи мозиллу")]
    [InlineData("запусти мозиллу")]
    [InlineData("включи mozilla")]
    [InlineData("включи пейнт")]
    [InlineData("включи озон")]
    [InlineData("включи заметки")]
    [InlineData("включи teams")]
    [InlineData("включи реестр")]
    [InlineData("открой переводчик")]
    [InlineData("открой gmail")]
    [InlineData("открой майл")]
    [InlineData("включи влс")]
    [InlineData("включи snip")]
    [InlineData("включи тытруба")]
    [InlineData("открой скайп")]
    [InlineData("открой твиттер")]
    [InlineData("запусти зум")]
    public void ИмяПрограммыОстаётсяЗапуском(string text)
    {
        var kind = CommandParser.Parse(text).Kind;

        Assert.True(kind == IntentKind.Launch,
            $"«{text}» разобралось как {kind} — команда перехватила запуск программы");
    }

    [Theory]
    // Голое слово командой не становится: там нет второго слова, которое
    // подтвердило бы догадку, а имён программ в одно слово полно.
    [InlineData("твиттер")]
    [InlineData("твитер")]
    [InlineData("twitter")]
    [InlineData("скайп")]
    [InlineData("вёрд")]
    [InlineData("скриншотер")]
    public void ОдноСловоНеСтановитсяКомандойПоСозвучию(string text)
    {
        var kind = CommandParser.Parse(text).Kind;

        Assert.True(kind is IntentKind.Launch or IntentKind.None,
            $"«{text}» разобралось как {kind}");
    }

    [Theory]
    // И при этом настоящие команды из одного слова обязаны работать.
    [InlineData("пауза", IntentKind.MediaPause)]
    [InlineData("дальше", IntentKind.MediaNext)]
    [InlineData("громче", IntentKind.VolumeUp)]
    [InlineData("тише", IntentKind.VolumeDown)]
    [InlineData("вниз", IntentKind.ScrollDown)]
    [InlineData("субтитры", IntentKind.MediaCaptions)]
    [InlineData("приблизь", IntentKind.ZoomIn)]
    [InlineData("скопируй", IntentKind.Copy)]
    [InlineData("вставь", IntentKind.Paste)]
    [InlineData("диктую", IntentKind.DictationStart)]
    public void НастоящиеОднословныеКомандыРаботают(string text, IntentKind expected)
    {
        Assert.Equal(expected, CommandParser.Parse(text).Kind);
    }
}
