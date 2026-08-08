using System.Net;
using Hika.Catalog;
using Hika.Config;
using Hika.Diagnostics;
using Hika.Nlu;

namespace Hika.Skills;

/// <summary>Направляет разобранное намерение в исполнителя.</summary>
public sealed class SkillRouter
{
    private readonly AppCatalog _catalog;

    public SkillRouter(AppCatalog catalog) => _catalog = catalog;

    public SkillResult Execute(Intent intent, BehaviorConfig behavior)
    {
        try
        {
            return intent.Kind switch
            {
                IntentKind.Launch => Launch(intent.Argument, behavior),
                IntentKind.Search => Search(intent.Argument, behavior),

                IntentKind.VolumeUp => SystemActions.VolumeUp(),
                IntentKind.VolumeDown => SystemActions.VolumeDown(),
                IntentKind.VolumeMute => SystemActions.ToggleMute(),

                IntentKind.MediaPlayPause => SystemActions.MediaPlayPause(),
                IntentKind.MediaNext => SystemActions.MediaNext(),
                IntentKind.MediaPrevious => SystemActions.MediaPrevious(),

                IntentKind.LockWorkstation => SystemActions.LockWorkstation(),
                IntentKind.ShowDesktop => SystemActions.ShowDesktop(),
                IntentKind.MinimizeWindow => SystemActions.MinimizeActiveWindow(),
                IntentKind.CloseWindow => SystemActions.CloseActiveWindow(),
                IntentKind.Screenshot => SystemActions.Screenshot(),

                _ => SkillResult.Fail("команда не распознана"),
            };
        }
        catch (Exception ex)
        {
            Log.Error($"исполнение намерения {intent} сорвалось", ex, "skills");
            return SkillResult.Fail("внутренняя ошибка");
        }
    }

    private SkillResult Launch(string phrase, BehaviorConfig behavior)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return SkillResult.Fail("нечего открывать");

        var match = _catalog.Resolve(phrase, behavior.MatchThreshold);
        if (match is not null) return Launcher.Launch(match.Entry);

        // Похоже на адрес сайта — открываем как есть.
        if (LooksLikeDomain(phrase))
            return Launcher.OpenUrl(phrase.Replace(" ", ""), phrase);

        // Ничего не нашли. Молча проглотить команду хуже, чем показать
        // результаты поиска: человек хотя бы увидит, что его услышали.
        if (behavior.WebSearchFallback)
        {
            Log.Info($"«{phrase}» в каталоге нет, ухожу в поиск", "skills");
            return Search(phrase, behavior);
        }

        return SkillResult.Fail($"«{phrase}» не найдено");
    }

    private static SkillResult Search(string query, BehaviorConfig behavior)
    {
        if (string.IsNullOrWhiteSpace(query)) return SkillResult.Fail("пустой запрос");

        var template = string.IsNullOrWhiteSpace(behavior.SearchUrl)
            ? "https://www.google.com/search?q={q}"
            : behavior.SearchUrl;

        var url = template.Replace("{q}", WebUtility.UrlEncode(query));
        return Launcher.OpenUrl(url, $"поиск «{query}»");
    }

    private static bool LooksLikeDomain(string phrase)
    {
        var compact = phrase.Replace(" ", "");
        if (!compact.Contains('.')) return false;
        if (compact.Length < 4 || compact.Length > 100) return false;

        var lastDot = compact.LastIndexOf('.');
        var tld = compact[(lastDot + 1)..];

        return tld.Length is >= 2 and <= 6 && tld.All(char.IsLetter);
    }
}
