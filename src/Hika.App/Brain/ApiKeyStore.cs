using System.Security.Cryptography;
using System.Text;
using Hika.Config;
using Hika.Diagnostics;

namespace Hika.Brain;

/// <summary>
/// Хранит ключ доступа к Claude.
///
/// В общий файл настроек он не попадает намеренно. config.json человек
/// открывает руками, показывает в переписке, кладёт в архив с логами —
/// и ключ уезжает вместе с ним. Поэтому ключ живёт отдельным файлом
/// и зашифрован средствами Windows так, что расшифровать его может только
/// эта учётная запись на этом компьютере. Скопированный файл на другой
/// машине бесполезен.
///
/// Переменная окружения ANTHROPIC_API_KEY, если она задана, важнее файла:
/// так принято, и человек, который её выставил, знает, зачем.
/// </summary>
public static class ApiKeyStore
{
    private static string Path => System.IO.Path.Combine(AppPaths.Root, "ключ.dat");

    /// <summary>Дополнительная примесь к шифрованию. От подмены файла не спасает, но связывает его с программой.</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HIKA-anthropic-key-v1");

    public static bool HasKey => !string.IsNullOrWhiteSpace(Read());

    public static string? Read()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment.Trim();

        try
        {
            if (!File.Exists(Path)) return null;

            var encrypted = File.ReadAllBytes(Path);
            var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain).Trim();
        }
        catch (Exception ex)
        {
            // Чаще всего это значит, что файл перенесли с другого компьютера
            // или из другой учётной записи. Расшифровать его нельзя ничем.
            Log.Warn($"ключ не читается ({ex.Message}) — введите его заново в настройках", "brain");
            return null;
        }
    }

    public static bool Write(string? key)
    {
        try
        {
            AppPaths.EnsureCreated();

            if (string.IsNullOrWhiteSpace(key))
            {
                if (File.Exists(Path)) File.Delete(Path);
                Log.Info("ключ удалён", "brain");
                return true;
            }

            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(key.Trim()), Entropy, DataProtectionScope.CurrentUser);

            File.WriteAllBytes(Path, encrypted);
            Log.Info("ключ сохранён", "brain");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("ключ не сохранился", ex, "brain");
            return false;
        }
    }

    /// <summary>Ключ в виде, пригодном для показа на экране: начало, хвост и точки между ними.</summary>
    public static string Masked()
    {
        var key = Read();
        if (string.IsNullOrEmpty(key)) return "не задан";
        if (key.Length <= 12) return "задан";

        return $"{key[..10]}…{key[^4..]}";
    }
}
