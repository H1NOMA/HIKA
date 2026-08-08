using System.Reflection;

namespace Hika.Diagnostics;

/// <summary>
/// Какая именно сборка сейчас запущена.
///
/// Нужно потому, что «не работает» и «не работает в той сборке, которую вы
/// думаете» — разные диагнозы, а отличить их по внешним признакам невозможно.
/// Сборочный сервер вписывает сюда хеш коммита, так что строку ниже можно
/// сверить с историей репозитория буква в букву.
/// </summary>
public static class BuildInfo
{
    /// <summary>Версия с хешем коммита, если он был вписан при сборке.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>Когда собран исполняемый файл.</summary>
    public static string Built { get; } = ReadBuildTime();

    public static string Describe() => $"HIKA {Version}, собрано {Built}";

    private static string ReadVersion()
    {
        try
        {
            var informational = typeof(BuildInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            return string.IsNullOrWhiteSpace(informational) ? "0.1.0" : informational;
        }
        catch
        {
            return "0.1.0";
        }
    }

    private static string ReadBuildTime()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "неизвестно";

            return File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm");
        }
        catch
        {
            return "неизвестно";
        }
    }
}
