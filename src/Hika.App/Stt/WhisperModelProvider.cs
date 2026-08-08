using Hika.Config;
using Hika.Diagnostics;

namespace Hika.Stt;

public sealed record ModelChoice(string Key, string FileName, long MinBytes, string Human);

/// <summary>
/// Находит и при необходимости скачивает модель Whisper.
///
/// Загрузчик написан вручную, а не взят из библиотеки, по двум причинам:
/// имена квантованных файлов у разных размеров модели не подчиняются одному
/// правилу (small живёт с суффиксом q5_1, а large — с q5_0), и нам нужен
/// откат на неквантованный файл, если квантованного в репозитории нет.
/// </summary>
public static class WhisperModelProvider
{
    private const string BaseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

    /// <summary>Зеркало на случай, если Hugging Face недоступен.</summary>
    private const string MirrorUrl = "https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main/";

    /// <summary>
    /// Варианты файлов для каждой пары «модель + квантование», в порядке предпочтения.
    /// Последним всегда идёт неквантованный файл — он есть всегда.
    /// </summary>
    private static readonly Dictionary<string, ModelChoice[]> Variants = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tiny"] = new[]
        {
            new ModelChoice("tiny", "ggml-tiny.bin", 60_000_000, "tiny (75 МБ)"),
        },
        ["tiny:q5_0"] = new[]
        {
            new ModelChoice("tiny", "ggml-tiny-q5_1.bin", 25_000_000, "tiny q5_1 (31 МБ)"),
            new ModelChoice("tiny", "ggml-tiny.bin", 60_000_000, "tiny (75 МБ)"),
        },
        ["base"] = new[]
        {
            new ModelChoice("base", "ggml-base.bin", 120_000_000, "base (142 МБ)"),
        },
        ["base:q5_0"] = new[]
        {
            new ModelChoice("base", "ggml-base-q5_1.bin", 45_000_000, "base q5_1 (57 МБ)"),
            new ModelChoice("base", "ggml-base.bin", 120_000_000, "base (142 МБ)"),
        },
        ["small"] = new[]
        {
            new ModelChoice("small", "ggml-small.bin", 420_000_000, "small (466 МБ)"),
        },
        ["small:q5_0"] = new[]
        {
            new ModelChoice("small", "ggml-small-q5_1.bin", 150_000_000, "small q5_1 (181 МБ)"),
            new ModelChoice("small", "ggml-small.bin", 420_000_000, "small (466 МБ)"),
        },
        ["small:q8_0"] = new[]
        {
            new ModelChoice("small", "ggml-small-q8_0.bin", 220_000_000, "small q8_0 (252 МБ)"),
            new ModelChoice("small", "ggml-small.bin", 420_000_000, "small (466 МБ)"),
        },
        ["medium"] = new[]
        {
            new ModelChoice("medium", "ggml-medium.bin", 1_400_000_000, "medium (1.5 ГБ)"),
        },
        ["medium:q5_0"] = new[]
        {
            new ModelChoice("medium", "ggml-medium-q5_0.bin", 480_000_000, "medium q5_0 (539 МБ)"),
            new ModelChoice("medium", "ggml-medium.bin", 1_400_000_000, "medium (1.5 ГБ)"),
        },
        ["largev3turbo"] = new[]
        {
            new ModelChoice("largev3turbo", "ggml-large-v3-turbo.bin", 1_500_000_000, "large-v3-turbo (1.6 ГБ)"),
        },
        ["largev3turbo:q5_0"] = new[]
        {
            new ModelChoice("largev3turbo", "ggml-large-v3-turbo-q5_0.bin", 500_000_000, "large-v3-turbo q5_0 (547 МБ)"),
            new ModelChoice("largev3turbo", "ggml-large-v3-turbo.bin", 1_500_000_000, "large-v3-turbo (1.6 ГБ)"),
        },
    };

    public static string ResolveDirectory(SpeechConfig cfg)
        => string.IsNullOrWhiteSpace(cfg.ModelDirectory) ? AppPaths.DefaultModelDirectory : cfg.ModelDirectory;

    private static ModelChoice[] Candidates(SpeechConfig cfg)
    {
        var model = (cfg.Model ?? "small").Trim().ToLowerInvariant().Replace("-", "").Replace("_", "");
        var quant = (cfg.Quantization ?? "none").Trim().ToLowerInvariant();

        if (quant is not ("none" or ""))
        {
            if (Variants.TryGetValue($"{model}:{quant}", out var q)) return q;
            // Просили квантование, которого для этой модели нет — берём q5_0, он определён для всех.
            if (Variants.TryGetValue($"{model}:q5_0", out var q5)) return q5;
        }

        if (Variants.TryGetValue(model, out var plain)) return plain;

        Log.Warn($"модель «{cfg.Model}» неизвестна, беру small", "stt");
        return Variants["small:q5_0"];
    }

    /// <summary>Путь к уже скачанной модели или null.</summary>
    public static string? FindLocal(SpeechConfig cfg)
    {
        var dir = ResolveDirectory(cfg);
        foreach (var c in Candidates(cfg))
        {
            var path = Path.Combine(dir, c.FileName);
            if (File.Exists(path) && new FileInfo(path).Length >= c.MinBytes) return path;
        }
        return null;
    }

    public static string DescribeChoice(SpeechConfig cfg) => Candidates(cfg)[0].Human;

    /// <param name="progress">Доля скачанного 0..1 и человекочитаемая подпись.</param>
    public static async Task<string?> EnsureAsync(
        SpeechConfig cfg,
        Action<double, string>? progress = null,
        CancellationToken ct = default)
    {
        var local = FindLocal(cfg);
        if (local is not null)
        {
            Log.Info($"модель распознавания: {Path.GetFileName(local)}", "stt");
            return local;
        }

        var dir = ResolveDirectory(cfg);
        try { Directory.CreateDirectory(dir); }
        catch (Exception ex)
        {
            Log.Error($"каталог моделей недоступен: {dir}", ex, "stt");
            return null;
        }

        foreach (var candidate in Candidates(cfg))
        {
            foreach (var host in new[] { BaseUrl, MirrorUrl })
            {
                if (ct.IsCancellationRequested) return null;

                var url = host + candidate.FileName;
                var target = Path.Combine(dir, candidate.FileName);

                try
                {
                    Log.Info($"скачиваю модель {candidate.Human}: {url}", "stt");
                    progress?.Invoke(0, $"Скачиваю {candidate.Human}");

                    if (await DownloadAsync(url, target, candidate.MinBytes, candidate.Human, progress, ct).ConfigureAwait(false))
                    {
                        Log.Info($"модель готова: {candidate.FileName}", "stt");
                        return target;
                    }
                }
                catch (OperationCanceledException) { return null; }
                catch (Exception ex)
                {
                    Log.Warn($"не вышло скачать {candidate.FileName} с {host}: {ex.Message}", "stt");
                }
            }
        }

        Log.Error("ни один вариант модели скачать не удалось", "stt");
        return null;
    }

    private static async Task<bool> DownloadAsync(
        string url, string target, long minBytes, string human,
        Action<double, string>? progress, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(45) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("HIKA/0.1");

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warn($"{url} -> HTTP {(int)response.StatusCode}", "stt");
            return false;
        }

        var total = response.Content.Headers.ContentLength ?? 0;
        var tmp = target + ".part";

        await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var sink = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
        {
            var buffer = new byte[1 << 20];
            long done = 0;
            var lastReport = DateTime.UtcNow;
            int read;

            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await sink.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;

                if ((DateTime.UtcNow - lastReport).TotalMilliseconds > 700)
                {
                    lastReport = DateTime.UtcNow;
                    var fraction = total > 0 ? (double)done / total : 0;
                    progress?.Invoke(fraction, $"{human}: {done / 1_048_576} МБ" + (total > 0 ? $" из {total / 1_048_576} МБ" : ""));
                    Log.Debug($"скачано {done / 1_048_576} МБ", "stt");
                }
            }
        }

        var size = new FileInfo(tmp).Length;
        if (size < minBytes)
        {
            Log.Warn($"скачанный файл слишком мал: {size} Б, ожидалось от {minBytes} Б", "stt");
            try { File.Delete(tmp); } catch { }
            return false;
        }

        File.Move(tmp, target, overwrite: true);
        progress?.Invoke(1, $"{human}: готово");
        return true;
    }
}
