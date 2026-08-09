using Hika.Catalog;
using Hika.Config;
using Hika.Diagnostics;

namespace Hika.Skills;

/// <summary>
/// «Включи музыку».
///
/// Просьба короткая, а стоит за ней три разных случая, и путать их нельзя.
///
/// Музыка приостановлена — надо просто продолжить. Это самый частый случай
/// и самый быстрый: плеер уже открыт, достаточно одной команды сеансу.
///
/// Плеер закрыт — надо его поднять и нажать «играть». Здесь и кроется
/// тонкость: сразу после запуска плеера нажимать некуда, сеанс появляется
/// через несколько секунд. Поэтому команда уходит в фон и ждёт, пока плеер
/// объявится, а человек тем временем занят своим.
///
/// Оговорка, которую честнее сказать вслух: выбрать конкретный плейлист
/// снаружи нельзя — плееры такого не позволяют никому. Но и Apple Music,
/// и Spotify по нажатию «играть» возвращаются к тому, что слушали
/// последним, а это ровно то, чего от них ждут.
/// </summary>
public sealed class MusicSkill
{
    private readonly AppCatalog _catalog;

    /// <summary>
    /// Чем играть, по убыванию предпочтения. Первое, что найдётся
    /// установленным, тем и будем пользоваться.
    /// </summary>
    private static readonly string[] KnownPlayers =
    {
        "apple music", "эпл мьюзик", "spotify", "спотифай",
        "яндекс музыка", "yandex music", "вк музыка", "звук",
        "aimp", "foobar2000", "музыка", "media player", "itunes",
    };

    /// <summary>Сколько ждать, пока запущенный плеер объявится в системе.</summary>
    private static readonly TimeSpan StartupWait = TimeSpan.FromSeconds(25);

    public MusicSkill(AppCatalog catalog) => _catalog = catalog;

    public SkillResult Play(BehaviorConfig behavior)
    {
        // Уже играет — вмешиваться незачем.
        if (MediaSessions.IsPlaying()) return SkillResult.Ok("уже играет");

        // Приостановлено — продолжаем. Это одно движение и ноль ожидания.
        if (MediaSessions.HasPausedSession())
        {
            var resumed = MediaSessions.Play();
            if (resumed.Success) return resumed;
        }

        var entry = FindPlayer(behavior);
        if (entry is null)
        {
            Log.Info("музыкального приложения в каталоге нет", "music");
            return SkillResult.Fail("не нашла, чем включить музыку");
        }

        var launched = Launcher.Launch(entry).From(entry);
        if (!launched.Success) return launched;

        // Плеер запущен, но нажимать пока некуда: сеанс воспроизведения
        // появляется через несколько секунд. Ждём в фоне — держать здесь
        // человека ради этого нельзя.
        StartWhenReady(entry.DisplayName);

        return SkillResult.Ok($"включаю музыку — {entry.DisplayName}").From(entry);
    }

    /// <summary>Какой программой играть музыку.</summary>
    private CatalogEntry? FindPlayer(BehaviorConfig behavior)
    {
        // Названное человеком важнее любых догадок.
        if (!string.IsNullOrWhiteSpace(behavior.MusicApp))
        {
            var chosen = _catalog.Resolve(behavior.MusicApp, 0.5);
            if (chosen is not null) return chosen.Entry;

            Log.Warn($"музыкальное приложение «{behavior.MusicApp}» не найдено, ищу сама", "music");
        }

        foreach (var name in KnownPlayers)
        {
            var match = _catalog.Resolve(name, 0.72);
            if (match is not null)
            {
                Log.Info($"музыку буду включать через {match.Entry.DisplayName}", "music");
                return match.Entry;
            }
        }

        return null;
    }

    private static void StartWhenReady(string appName)
    {
        _ = Task.Run(async () =>
        {
            var deadline = DateTime.UtcNow + StartupWait;

            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(600).ConfigureAwait(false);

                if (MediaSessions.IsPlaying())
                {
                    Log.Info($"{appName} заиграл сам", "music");
                    return;
                }

                if (!MediaSessions.HasAnySession()) continue;

                var result = MediaSessions.Play();
                Log.Info($"{appName}: {result.Description}", "music");
                return;
            }

            Log.Info($"{appName} так и не объявился — играть нечем", "music");
        });
    }
}
