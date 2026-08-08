using System.Text.Json;
using Hika.Config;
using Hika.Diagnostics;
using Hika.Nlu;

namespace Hika.Catalog;

/// <summary>
/// Всё, что HIKA умеет открывать: встроенный каталог, пользовательские
/// дополнения и всё, что нашлось установленным в системе.
///
/// Список установленных программ обновляется в фоне и подменяется целиком,
/// чтобы распознавание команды никогда не ждало обхода диска.
/// </summary>
public sealed class AppCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private IReadOnlyList<CatalogEntry> _builtin = Array.Empty<CatalogEntry>();
    private IReadOnlyList<CatalogEntry> _user = Array.Empty<CatalogEntry>();
    private IReadOnlyList<CatalogEntry> _installed = Array.Empty<CatalogEntry>();

    private volatile CatalogEntry[] _all = Array.Empty<CatalogEntry>();

    public int Count => _all.Length;
    public int BuiltinCount => _builtin.Count;
    public int InstalledCount => _installed.Count;
    public IReadOnlyList<CatalogEntry> Entries => _all;

    public void Load(HikaConfig config)
    {
        _builtin = LoadBuiltin();
        _user = LoadUser(config);
        Rebuild();

        Log.Info($"каталог: {_builtin.Count} встроенных, {_user.Count} своих", "catalog");
    }

    private static IReadOnlyList<CatalogEntry> LoadBuiltin()
    {
        var path = Path.Combine(AppPaths.InstallDirectory, "Catalog", "catalog.default.json");
        if (!File.Exists(path)) path = Path.Combine(AppPaths.InstallDirectory, "catalog.default.json");

        if (!File.Exists(path))
        {
            Log.Error($"встроенный каталог не найден: {path}", "catalog");
            return Array.Empty<CatalogEntry>();
        }

        try
        {
            var dto = JsonSerializer.Deserialize<CatalogFileDto>(File.ReadAllText(path), JsonOptions);
            if (dto is null) return Array.Empty<CatalogEntry>();

            return dto.Entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Command) && e.Names.Count > 0)
                .Select(e => CatalogEntry.FromDto(
                    e,
                    e.Kind.Equals("site", StringComparison.OrdinalIgnoreCase) ? EntryKind.Site : EntryKind.App,
                    weight: 1.0))
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error("встроенный каталог не разобрался", ex, "catalog");
            return Array.Empty<CatalogEntry>();
        }
    }

    private static IReadOnlyList<CatalogEntry> LoadUser(HikaConfig config)
    {
        var result = new List<CatalogEntry>();

        // Пользовательский файл каталога — того же формата, что встроенный.
        try
        {
            if (File.Exists(AppPaths.CatalogFile))
            {
                var dto = JsonSerializer.Deserialize<CatalogFileDto>(File.ReadAllText(AppPaths.CatalogFile), JsonOptions);
                if (dto is not null)
                {
                    foreach (var e in dto.Entries)
                    {
                        if (string.IsNullOrWhiteSpace(e.Command) || e.Names.Count == 0) continue;
                        result.Add(CatalogEntry.FromDto(
                            e,
                            e.Kind.Equals("site", StringComparison.OrdinalIgnoreCase) ? EntryKind.Site : EntryKind.Custom,
                            // Своё всегда важнее встроенного: если человек описал
                            // команду руками, он имел в виду именно её.
                            weight: 1.12));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("свой каталог не разобрался", ex, "catalog");
        }

        // Короткая форма прямо в настройках.
        foreach (var custom in config.Custom ?? new List<CustomEntry>())
        {
            if (!custom.IsValid) continue;

            result.Add(CatalogEntry.Create(
                id: "custom:" + custom.Phrases[0],
                kind: EntryKind.Custom,
                command: custom.Target,
                names: custom.Phrases,
                args: custom.Arguments,
                weight: 1.12));
        }

        return result;
    }

    /// <summary>
    /// Подменяет список установленного в системе. Кто именно его собрал,
    /// каталогу неизвестно — обход диска живёт в части, привязанной к Windows.
    ///
    /// Список заменяется целиком и одним присваиванием, поэтому распознавание
    /// команды никогда не ждёт обновления и не видит его наполовину.
    /// </summary>
    public void SetInstalled(IReadOnlyList<CatalogEntry> installed)
    {
        _installed = installed;
        Rebuild();

        Log.Info($"каталог обновлён: всего записей {_all.Length}", "catalog");
    }

    private void Rebuild()
    {
        // Порядок задаёт приоритет при равных оценках: своё, потом выверенное
        // встроенное, потом всё найденное в системе.
        var combined = new List<CatalogEntry>(_user.Count + _builtin.Count + _installed.Count);
        combined.AddRange(_user);
        combined.AddRange(_builtin);
        combined.AddRange(_installed);

        _all = combined.ToArray();
    }

    /// <summary>
    /// Ищет, что человек имел в виду. Возвращает null, если ничего
    /// достаточно похожего не нашлось.
    /// </summary>
    public CatalogMatch? Resolve(string phrase, double threshold)
    {
        var tokens = TextNormalizer.Tokenize(phrase);
        if (tokens.Length == 0) return null;

        var spokenKeys = tokens.Select(Translit.Keys).ToArray();

        var snapshot = _all;
        CatalogEntry? best = null;
        double bestScore = 0;

        foreach (var entry in snapshot)
        {
            var keys = entry.NameKeys;
            for (int i = 0; i < keys.Length; i++)
            {
                var score = FuzzyMatch.PhraseSimilarity(spokenKeys, keys[i]) * entry.Weight;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = entry;
                }
            }
        }

        if (best is null || bestScore < threshold)
        {
            Log.Debug($"«{phrase}» -> ничего подходящего (лучшая оценка {bestScore:F2})", "catalog");
            return null;
        }

        Log.Debug($"«{phrase}» -> {best.DisplayName} ({best.Kind}, оценка {bestScore:F2})", "catalog");
        return new CatalogMatch(best, bestScore);
    }

    /// <summary>Несколько лучших вариантов — для отчёта диагностики.</summary>
    public IReadOnlyList<CatalogMatch> Top(string phrase, int count = 5)
    {
        var tokens = TextNormalizer.Tokenize(phrase);
        if (tokens.Length == 0) return Array.Empty<CatalogMatch>();

        var spokenKeys = tokens.Select(Translit.Keys).ToArray();

        return _all
            .Select(entry =>
            {
                double best = 0;
                foreach (var keys in entry.NameKeys)
                {
                    var score = FuzzyMatch.PhraseSimilarity(spokenKeys, keys) * entry.Weight;
                    if (score > best) best = score;
                }
                return new CatalogMatch(entry, best);
            })
            .OrderByDescending(m => m.Score)
            .Take(count)
            .ToList();
    }
}
