using System.Text.Json.Serialization;
using Hika.Nlu;

namespace Hika.Catalog;

public enum EntryKind
{
    /// <summary>Программа из встроенного каталога.</summary>
    App,

    /// <summary>Сайт.</summary>
    Site,

    /// <summary>Найдено в меню «Пуск» или среди установленных приложений Windows.</summary>
    Installed,

    /// <summary>Добавлено пользователем.</summary>
    Custom,
}

/// <summary>Запись каталога в том виде, в каком она лежит в JSON.</summary>
public sealed class CatalogEntryDto
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "app";

    /// <summary>Что передать в ShellExecute: имя программы, ссылка, ms-settings: и тому подобное.</summary>
    public string Command { get; set; } = "";

    public string Args { get; set; } = "";

    /// <summary>Запасные полные пути, если по имени программу найти не удалось. Переменные окружения раскрываются.</summary>
    public List<string> Paths { get; set; } = new();

    /// <summary>Как это называют голосом.</summary>
    public List<string> Names { get; set; } = new();
}

public sealed class CatalogFileDto
{
    public List<CatalogEntryDto> Entries { get; set; } = new();
}

/// <summary>
/// Запись каталога, готовая к сопоставлению. Все написания названий
/// разобраны заранее: сравнение идёт на каждой команде, и считать
/// транслитерацию заново каждый раз незачем.
/// </summary>
public sealed class CatalogEntry
{
    public required string Id { get; init; }
    public required EntryKind Kind { get; init; }
    public required string Command { get; init; }
    public string Args { get; init; } = "";
    public IReadOnlyList<string> Paths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Names { get; init; } = Array.Empty<string>();

    /// <summary>Каждое название, разбитое на слова.</summary>
    [JsonIgnore]
    public required IReadOnlyList<string[]> NameTokens { get; init; }

    /// <summary>
    /// Написания каждого слова каждого названия, посчитанные заранее.
    /// Сопоставление идёт по всему каталогу на каждой команде, и пересчитывать
    /// транслитерацию тысяч записей при каждом «открой ютуб» было бы расточительно.
    /// </summary>
    [JsonIgnore]
    public string[][][] NameKeys { get; private set; } = Array.Empty<string[][]>();

    private void BuildKeys()
    {
        NameKeys = NameTokens
            .Select(tokens => tokens.Select(Translit.Keys).ToArray())
            .ToArray();
    }

    /// <summary>Понижающий коэффициент: встроенные записи должны выигрывать у случайных ярлыков.</summary>
    [JsonIgnore]
    public double Weight { get; init; } = 1.0;

    public string DisplayName => Names.Count > 0 ? Names[0] : Id;

    public static CatalogEntry FromDto(CatalogEntryDto dto, EntryKind kind, double weight = 1.0)
    {
        var names = dto.Names
            .Select(TextNormalizer.Normalize)
            .Where(n => n.Length > 0)
            .Distinct()
            .ToList();

        var entry = new CatalogEntry
        {
            Id = string.IsNullOrWhiteSpace(dto.Id) ? (names.FirstOrDefault() ?? dto.Command) : dto.Id,
            Kind = kind,
            Command = dto.Command,
            Args = dto.Args ?? "",
            Paths = dto.Paths ?? new List<string>(),
            Names = names,
            NameTokens = names.Select(n => n.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList(),
            Weight = weight,
        };

        entry.BuildKeys();
        return entry;
    }

    public static CatalogEntry Create(
        string id, EntryKind kind, string command, IEnumerable<string> names,
        string args = "", IReadOnlyList<string>? paths = null, double weight = 1.0)
    {
        var normalized = names
            .Select(TextNormalizer.Normalize)
            .Where(n => n.Length > 0)
            .Distinct()
            .ToList();

        var entry = new CatalogEntry
        {
            Id = id,
            Kind = kind,
            Command = command,
            Args = args,
            Paths = paths ?? Array.Empty<string>(),
            Names = normalized,
            NameTokens = normalized.Select(n => n.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList(),
            Weight = weight,
        };

        entry.BuildKeys();
        return entry;
    }
}

public sealed record CatalogMatch(CatalogEntry Entry, double Score);
