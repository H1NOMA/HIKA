using Hika.Config;
using Hika.Diagnostics;

namespace Hika.Vad;

/// <summary>
/// Достаёт файл модели Silero VAD: ищет локально, иначе качает.
/// Файл маленький (около двух мегабайт), поэтому скачивание при первом
/// запуске проходит незаметно — а до его окончания работает запасной детектор.
/// </summary>
public static class SileroModelProvider
{
    private const string FileName = "silero_vad.onnx";

    private static readonly string[] Sources =
    {
        "https://raw.githubusercontent.com/snakers4/silero-vad/master/src/silero_vad/data/silero_vad.onnx",
        "https://github.com/snakers4/silero-vad/raw/master/src/silero_vad/data/silero_vad.onnx",
        "https://raw.githubusercontent.com/snakers4/silero-vad/v5.1.2/src/silero_vad/data/silero_vad.onnx",
    };

    public static string ExpectedPath(string? modelDirectory = null)
        => Path.Combine(
            string.IsNullOrWhiteSpace(modelDirectory) ? AppPaths.DefaultModelDirectory : modelDirectory!,
            FileName);

    /// <summary>Возвращает путь к модели или null, если её нет и скачать не вышло.</summary>
    public static async Task<string?> EnsureAsync(string? modelDirectory, CancellationToken ct = default)
    {
        var dir = string.IsNullOrWhiteSpace(modelDirectory) ? AppPaths.DefaultModelDirectory : modelDirectory!;
        var path = Path.Combine(dir, FileName);

        try
        {
            Directory.CreateDirectory(dir);

            // Файл меньше мегабайта почти наверняка обрывок прошлой закачки.
            if (File.Exists(path) && new FileInfo(path).Length > 900_000) return path;
            if (File.Exists(path)) { try { File.Delete(path); } catch { } }
        }
        catch (Exception ex)
        {
            Log.Warn($"каталог моделей недоступен: {ex.Message}", "vad");
            return null;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("HIKA/0.1");

        foreach (var url in Sources)
        {
            if (ct.IsCancellationRequested) return null;

            try
            {
                Log.Info($"скачиваю модель детектора речи: {url}", "vad");

                var bytes = await http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
                if (bytes.Length < 900_000)
                {
                    Log.Warn($"файл подозрительно мал ({bytes.Length} Б), пробую другой источник", "vad");
                    continue;
                }

                var tmp = path + ".part";
                await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
                File.Move(tmp, path, overwrite: true);

                Log.Info($"модель детектора речи готова ({bytes.Length / 1024} КБ)", "vad");
                return path;
            }
            catch (Exception ex)
            {
                Log.Warn($"источник не сработал ({url}): {ex.Message}", "vad");
            }
        }

        Log.Warn("модель Silero скачать не удалось — остаёмся на запасном детекторе", "vad");
        return null;
    }
}
