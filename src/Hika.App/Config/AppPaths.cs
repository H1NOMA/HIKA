namespace Hika.Config;

/// <summary>Все пути, по которым HIKA что-то хранит. Ничего не пишется рядом с exe.</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HIKA");

    public static string ConfigFile => Path.Combine(Root, "config.json");
    public static string CatalogFile => Path.Combine(Root, "catalog.json");
    public static string LogDirectory => Path.Combine(Root, "logs");
    public static string DefaultModelDirectory => Path.Combine(Root, "models");
    public static string CacheDirectory => Path.Combine(Root, "cache");

    /// <summary>Каталог рядом с исполняемым файлом — там лежит поставляемый по умолчанию catalog.default.json.</summary>
    public static string InstallDirectory { get; } =
        Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory) ?? AppContext.BaseDirectory;

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(CacheDirectory);
    }
}
