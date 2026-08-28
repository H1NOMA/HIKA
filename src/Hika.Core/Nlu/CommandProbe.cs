using Hika.Catalog;
using Hika.Config;
using Hika.Wake;

namespace Hika.Nlu;

/// <summary>Что HIKA сделала бы с этой фразой.</summary>
public sealed record ProbeResult(
    string Name,
    double NameScore,
    string Command,
    Intent Intent,
    string Target,
    double TargetScore,
    string Verdict);

/// <summary>
/// Разбор фразы без её исполнения.
///
/// Отвечает на вопрос, который иначе выясняется только опытом: «а что она
/// сделает, если я скажу вот так?». До сих пор единственным способом узнать
/// это было сказать вслух и посмотреть — то есть в половине случаев получить
/// открывшееся не то и потом это закрывать.
///
/// Особенно нужно там, где ошибка не видна: человек добавил свою команду
/// «открой смету» и хочет убедиться, что она не спутается с «открой смайлики»,
/// не запуская ни того ни другого. И там, где сказанное вообще не воспроизвести:
/// в списке услышанного лежит фраза, которую распознаватель исковеркал,
/// и произнести её ещё раз так же человек не сумеет.
///
/// Ничего не выполняет и выполнить не может: у неё нет исполнителя команд,
/// только разбор и каталог.
/// </summary>
public static class CommandProbe
{
    public static ProbeResult Explain(string phrase, WakeWordMatcher? wake, AppCatalog? catalog, HikaConfig config)
    {
        phrase = (phrase ?? "").Trim();

        if (phrase.Length == 0)
            return new ProbeResult("", 0, "", Intent.None, "", 0, "Пусто — скажите или впишите фразу.");

        var match = wake?.Match(phrase);

        // Имя не обязательно: человек проверяет команду, а не своё
        // произношение. Но сказать, что вживую эта фраза прошла бы мимо,
        // всё равно надо — иначе «в окошке работает, а вслух нет».
        var named = match?.Matched == true;
        var command = named ? match!.Rest : phrase;
        var name = named ? match!.Word : "";
        var nameScore = match?.Score ?? 0;

        if (command.Trim().Length == 0)
        {
            return new ProbeResult(name, nameScore, "", Intent.None, "", 0,
                "Только имя, без команды — я бы просто подождала продолжения.");
        }

        var intent = CommandParser.Parse(command);

        var target = "";
        double targetScore = 0;

        if (intent.Kind is IntentKind.Launch or IntentKind.Search && intent.Argument.Length > 0)
        {
            var found = catalog?.Resolve(intent.Argument, config.Behavior.MatchThreshold);

            if (found is not null)
            {
                target = found.Entry.DisplayName;
                targetScore = found.Score;
            }
        }

        return new ProbeResult(name, nameScore, command, intent, target, targetScore,
            Verdict(named, command, intent, target, targetScore, config));
    }

    private static string Verdict(
        bool named, string command, Intent intent, string target, double targetScore, HikaConfig config)
    {
        var prefix = named ? "" : "Имя не прозвучало — вслух я бы это пропустила. Но разбираю так: ";

        if (intent.Kind == IntentKind.None)
        {
            return prefix + (config.Behavior.WebSearchFallback
                ? "команду не разобрала — поискала бы это в интернете."
                : "команду не разобрала и ничего бы не сделала.");
        }

        if (intent.Kind is IntentKind.Launch or IntentKind.Search)
        {
            if (target.Length > 0)
                return prefix + $"открыла бы {target} (похоже на {targetScore:0.00}).";

            if (intent.ExplicitVerb)
                return prefix + $"«{intent.Argument}» в каталоге нет. Впишите это в «Свои команды».";

            return prefix + (config.Behavior.WebSearchFallback
                ? $"«{intent.Argument}» в каталоге нет — поискала бы в интернете."
                : $"«{intent.Argument}» в каталоге нет и ничего бы не сделала.");
        }

        return prefix + IntentNames.Describe(intent) + ".";
    }
}
