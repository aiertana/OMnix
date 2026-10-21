using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OMniX.WinUI.Services;

/// <summary>
///     Wuwa_Mod_Fixer 的一个发布版本
/// </summary>
public sealed record ModFixerRelease(
    string Tag,
    DateTimeOffset PublishedAt,
    string Notes,
    string AssetName,
    string DownloadUrl,
    long Size)
{
    public string DateText => PublishedAt.LocalDateTime.ToString("yyyy/MM/dd");

    public string SizeText => $"{Size / 1024.0 / 1024.0:F1} MB";
}

/// <summary>
///     二开：Mod 修复器。不再内置修复逻辑，改为从 Moonholder/Wuwa_Mod_Fixer 拉最新版并下载运行。
/// </summary>
public sealed class ModFixerService
{
    private const string ApiUrl = "https://api.github.com/repos/Moonholder/Wuwa_Mod_Fixer/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private ModFixerService()
    {
    }

    public static ModFixerService Instance { get; } = new();

    /// <summary>
    ///     上次查到的版本，用于按钮上冒提示气泡
    /// </summary>
    public ModFixerRelease? Latest { get; private set; }

    public string CacheRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OMniX", "ModFixer");

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OMniX");
        return client;
    }

    /// <summary>
    ///     查最新版本。网络失败返回 null，不影响主流程。
    /// </summary>
    public async Task<ModFixerRelease?> GetLatestReleaseAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await Http.GetStringAsync(ApiUrl, ct).ConfigureAwait(false);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var tag = root.GetProperty("tag_name").GetString();
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            var publishedAt = root.TryGetProperty("published_at", out var published)
                ? published.GetDateTimeOffset()
                : DateTimeOffset.MinValue;

            var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty;

            if (!root.TryGetProperty("assets", out var assets))
                return null;

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? string.Empty;

                // 只要 Windows 版 exe，不要 zip / linux 包
                if (!name.StartsWith("Wuwa_Mod_Fixer", StringComparison.OrdinalIgnoreCase) ||
                    !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                var url = asset.GetProperty("browser_download_url").GetString();
                var size = asset.TryGetProperty("size", out var sizeElement) ? sizeElement.GetInt64() : 0;

                if (string.IsNullOrWhiteSpace(url))
                    continue;

                Latest = new ModFixerRelease(tag, publishedAt, notes, name, url, size);
                return Latest;
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string GetLocalExePath(ModFixerRelease release)
    {
        return Path.Combine(CacheRoot, release.Tag, release.AssetName);
    }

    public bool IsDownloaded(ModFixerRelease release)
    {
        var path = GetLocalExePath(release);
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    /// <summary>
    ///     下载到本地缓存目录，带进度回调
    /// </summary>
    public async Task<string> DownloadAsync(ModFixerRelease release,
        IProgress<(double Percent, long Received, long Total)>? progress, CancellationToken ct = default)
    {
        var target = GetLocalExePath(release);

        if (IsDownloaded(release))
        {
            progress?.Report((100, release.Size, release.Size));
            return target;
        }

        var directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);

        var temp = target + ".part";

        using var response = await Http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? release.Size;
        var received = 0L;

        await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var destination = File.Create(temp))
        {
            var buffer = new byte[81920];
            int read;

            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                received += read;

                progress?.Report((total > 0 ? received * 100.0 / total : 0, received, total));
            }
        }

        if (File.Exists(target))
            File.Delete(target);

        File.Move(temp, target);

        progress?.Report((100, received, total));
        return target;
    }

    public void Launch(string exePath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? CacheRoot,
            UseShellExecute = true
        });
    }

    /// <summary>
    ///     二开：修复器自己的配置文件（它会在启动时读 last_folder 作为「MOD 目录」）。
    /// </summary>
    private static string FixerSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WuwaModFixer", "settings.json");

    /// <summary>
    ///     二开：把「MOD 目录」预先填成指定 Mod 的文件夹，下次启动修复器就直接指向它。
    ///     只改 last_folder 这一个字段，窗口大小、语言这些保持原样。
    /// </summary>
    public bool SetLastFolder(string modFolderPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(FixerSettingsPath);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            JsonObject root;

            if (File.Exists(FixerSettingsPath))
            {
                var existing = File.ReadAllText(FixerSettingsPath);

                root = string.IsNullOrWhiteSpace(existing)
                    ? new JsonObject()
                    : JsonNode.Parse(existing) as JsonObject ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }

            root["last_folder"] = modFolderPath;

            File.WriteAllText(FixerSettingsPath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    ///     二开：把修复器的「MOD 目录」设成这个 Mod，然后启动它。
    ///     没下载过就先下载（无进度条，第一次会稍慢）。
    /// </summary>
    public async Task<ModFixerRelease?> LaunchForModAsync(string modFolderPath, CancellationToken ct = default)
    {
        var release = Latest ?? await GetLatestReleaseAsync(ct).ConfigureAwait(false);

        if (release is null)
            return null;

        if (!IsDownloaded(release))
            await DownloadAsync(release, null, ct).ConfigureAwait(false);

        // 二开：修复器只在启动时读一次「MOD 目录」，已经在跑的话新路径不会生效，
        // 所以先关掉它，再带着新路径重新打开。
        CloseRunningFixers();

        SetLastFolder(modFolderPath);
        Launch(GetLocalExePath(release));

        return release;
    }

    /// <summary>
    ///     二开：关掉已经在运行的 Wuwa_Mod_Fixer（名字里带版本号，所以按前缀匹配）。
    /// </summary>
    private static void CloseRunningFixers()
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!process.ProcessName.StartsWith("Wuwa_Mod_Fixer", StringComparison.OrdinalIgnoreCase))
                    continue;

                process.Kill();
                process.WaitForExit(3000);
            }
            catch
            {
                // 关不掉就算了，尽力而为
            }
        }
    }

    /// <summary>
    ///     把 markdown 更新日志压成可读纯文本
    /// </summary>
    public static string FormatNotes(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return "（这次更新没有写说明）";

        var lines = notes
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(line =>
            {
                var text = line.TrimEnd();

                if (text.StartsWith("###"))
                    return text.TrimStart('#', ' ').Trim();

                if (text.StartsWith("##"))
                    return text.TrimStart('#', ' ').Trim();

                if (text.StartsWith("* ") || text.StartsWith("- "))
                    return "· " + text[2..].Trim();

                return text;
            });

        var result = string.Join('\n', lines).Trim();

        return result.Length > 1200 ? result[..1200] + "\n……" : result;
    }
}
