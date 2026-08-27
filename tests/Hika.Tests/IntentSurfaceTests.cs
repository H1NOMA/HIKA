using System.Reflection;
using Hika.Nlu;
using Xunit;

namespace Hika.Tests;

/// <summary>
/// Каждое намерение должно быть чем-то произносимо.
///
/// Проверка ловит одну конкретную небрежность, которая иначе не ловится
/// ничем: намерение добавлено, исполнитель для него написан, а фразы,
/// которой его позвать, нет. Снаружи это выглядит как «функция есть,
/// но не работает» — и разбирается только чтением кода.
/// </summary>
public class IntentSurfaceTests
{
    /// <summary>
    /// Намерения, у которых нет и не должно быть своих фраз: они рождаются
    /// из разбора, а не из совпадения со списком.
    /// </summary>
    private static readonly HashSet<IntentKind> FromParser = new()
    {
        IntentKind.None,        // ничего не разобралось
        IntentKind.Launch,      // всё, что осталось после глагола запуска
        IntentKind.Search,      // «загугли …», «что такое …»
        IntentKind.FocusWindow, // «переключись на …»
        IntentKind.TypeText,    // «напечатай …»
        IntentKind.VolumeSet,   // громкость числом
        IntentKind.BrightnessSet,
        IntentKind.Timer,       // таймер числом
    };

    [Fact]
    public void УКаждогоНамеренияЕстьФраза()
    {
        var covered = CommandCatalog.All.Select(p => p.Kind).ToHashSet();

        var orphans = Enum.GetValues<IntentKind>()
            .Where(kind => !covered.Contains(kind))
            .Where(kind => !FromParser.Contains(kind))
            .Where(kind => !SaidByFixedCommand(kind))
            .ToArray();

        Assert.True(orphans.Length == 0,
            "намерения без единой фразы, которой их позвать: " + string.Join(", ", orphans));
    }

    [Fact]
    public void КаждаяФразаИзКаталогаУзнаётсяСамойСобой()
    {
        // Слот, в котором ошиблись буквой, молча перестаёт совпадать
        // с чем бы то ни было. Никакой другой проверкой это не видно:
        // команда просто тихо исчезает из программы.
        var broken = new List<string>();

        foreach (var kind in CommandCatalog.All.Select(p => p.Kind).Distinct())
        {
            var said = FirstPhrase(kind);
            if (said is null) continue;

            if (CommandParser.Parse(said).Kind != kind)
                broken.Add($"{kind}: «{said}» -> {CommandParser.Parse(said).Kind}");
        }

        Assert.True(broken.Count == 0, "шаблоны, не узнающие сами себя:\n" + string.Join("\n", broken));
    }

    /// <summary>
    /// Собирает простейшую фразу из обязательных слотов шаблона: по первому
    /// слову из каждого. Получается коряво, но это ровно то, что шаблон
    /// обязан узнавать.
    /// </summary>
    private static string? FirstPhrase(IntentKind kind)
    {
        var pattern = CommandCatalog.All.FirstOrDefault(p => p.Kind == kind);
        if (pattern is null) return null;

        var words = new List<string>();

        foreach (var slot in pattern.Slots)
        {
            if (slot.Optional) continue;

            var first = slot.Keys.FirstOrDefault();
            if (first is null || first.Length == 0) return null;

            // В слоте лежат ключи звучания, а не исходные слова: берём
            // кириллический вариант, он же первый.
            words.Add(first[0]);
        }

        return words.Count == 0 ? null : string.Join(' ', words);
    }

    /// <summary>Намерение встречается среди готовых фраз разбора.</summary>
    private static bool SaidByFixedCommand(IntentKind kind)
    {
        var field = typeof(CommandParser).GetField("FixedCommands",
            BindingFlags.NonPublic | BindingFlags.Static);

        if (field?.GetValue(null) is not Array table) return false;

        foreach (var row in table)
        {
            var property = row?.GetType().GetProperty("Kind") ?? row?.GetType().GetField("Kind") as MemberInfo;

            var value = property switch
            {
                PropertyInfo p => p.GetValue(row),
                FieldInfo f => f.GetValue(row),
                _ => null,
            };

            if (value is IntentKind found && found == kind) return true;
        }

        return false;
    }
}
