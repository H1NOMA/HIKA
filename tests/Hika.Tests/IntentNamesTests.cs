using Hika.Nlu;
using Xunit;

namespace Hika.Tests;

/// <summary>
/// Как команда называется для человека.
///
/// Список услышанного в окне настроек — единственный ответ на вопрос
/// «почему она открыла не то», который человек может получить сам.
/// Написан он был на языке, которого он не знает: «MediaSeekForwardFar»,
/// «OpenTaskManager». Показать не-программисту английское имя перечисления
/// значит показать ему, что здесь не для него.
///
/// Этот тест держит перевод полным: новое намерение без имени провалит
/// сборку раньше, чем доедет до человека.
/// </summary>
public class IntentNamesTests
{
    [Fact]
    public void УКаждогоНамеренияЕстьРусскоеИмя()
    {
        var безымянные = Enum.GetValues<IntentKind>()
            .Where(kind => IntentNames.Describe(kind) == kind.ToString())
            .ToArray();

        Assert.True(безымянные.Length == 0,
            "Без русского имени остались: " + string.Join(", ", безымянные));
    }

    [Fact]
    public void ИмяНеПустоеИНеАнглийское()
    {
        foreach (var kind in Enum.GetValues<IntentKind>())
        {
            var имя = IntentNames.Describe(kind);

            Assert.False(string.IsNullOrWhiteSpace(имя), $"{kind}: пустое имя");

            // Хотя бы одна кириллическая буква. Чисто латинские имена вроде
            // «Enter» и «Tab» разрешены нарочно: так они и написаны
            // на клавиатуре, и переводить их значит запутать.
            var латиница = имя.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
            var разрешено = имя is "Enter" or "Escape" or "Tab" or "Backspace" or "Delete";

            Assert.True(!латиница || разрешено, $"{kind}: имя «{имя}» не по-русски");
        }
    }

    [Fact]
    public void АргументПопадаетВОписание()
    {
        var описание = IntentNames.Describe(new Intent(IntentKind.Launch, "ютуб"));

        Assert.Equal("запуск: ютуб", описание);
    }

    [Fact]
    public void БезАргументаЛишнегоДвоеточияНет()
        => Assert.Equal("пауза", IntentNames.Describe(new Intent(IntentKind.MediaPause)));
}
