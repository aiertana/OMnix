using System.Text.Json;
using Serilog;

namespace OMniX.Core.Services.GameBanana;

/// <summary>
///     GameBanana 访问代理设置（参考 d3dxSkinManage-Plus 的 utils/proxy.py）：
///     无代理 / 跟随系统 / 手动填 URL。设置持久化到 LocalApplicationData，
///     DI 的 primary handler 每次重建时读取，所以改完设置重开商店窗口即生效。
///     没有代理的用户（没开 VPN / 没设系统代理）靠手动模式填本地加速端口。
/// </summary>
public static class GameBananaProxy
{
    private static readonly ILogger Logger = Log.ForContext(typeof(GameBananaProxy));

    public enum ProxyMode
    {
        None,
        System,
        Manual
    }

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OMniX", "gamebanana_proxy.json");

    public static ProxyMode Mode { get; set; } = ProxyMode.None;

    /// <summary>手动模式下的代理地址，形如 http://127.0.0.1:7890。</summary>
    public static string ManualUrl { get; set; } = "";

    /// <summary>当前生效的代理地址；没启用代理返回 null。</summary>
    public static Uri? GetProxyUri() => Mode switch
    {
        ProxyMode.System => GetSystemProxyUri(),
        ProxyMode.Manual when Uri.TryCreate(ManualUrl, UriKind.Absolute, out var manual)
                              && manual.Scheme is "http" or "https" => manual,
        _ => null
    };

    /// <summary>读 Windows 注册表的系统代理（HKCU Internet Settings），照原项目的解析逻辑。</summary>
    public static Uri? GetSystemProxyUri()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            if (key is null)
            {
                return null;
            }

            var enable = key.GetValue("ProxyEnable") as int?;
            if (enable is not 1)
            {
                return null;
            }

            var server = key.GetValue("ProxyServer") as string;
            if (string.IsNullOrWhiteSpace(server))
            {
                return null;
            }

            // 多协议写法（http=...;https=...）优先取 https，其次 http
            if (server.Contains('='))
            {
                var parts = server.Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Split('=', 2))
                    .Where(p => p.Length == 2)
                    .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
                server = parts.TryGetValue("https", out var https) ? https
                    : parts.TryGetValue("http", out var http) ? http
                    : parts.Values.FirstOrDefault() ?? "";
            }

            if (string.IsNullOrWhiteSpace(server))
            {
                return null;
            }

            if (!server.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !server.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && !server.StartsWith("socks", StringComparison.OrdinalIgnoreCase))
            {
                server = $"http://{server}";
            }

            return Uri.TryCreate(server, UriKind.Absolute, out var uri) ? uri : null;
        }
        catch (Exception e)
        {
            Logger.Warning(e, "读取系统代理失败");
            return null;
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(new ProxySnapshot(Mode.ToString(), ManualUrl)));
        }
        catch (Exception e)
        {
            Logger.Warning(e, "保存代理设置失败");
        }
    }

    public static void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return;
            }

            var snapshot = JsonSerializer.Deserialize<ProxySnapshot>(File.ReadAllText(SettingsPath));
            if (snapshot is null)
            {
                return;
            }

            Mode = Enum.TryParse<ProxyMode>(snapshot.Mode, out var mode) ? mode : ProxyMode.None;
            ManualUrl = snapshot.ManualUrl ?? "";
        }
        catch (Exception e)
        {
            Logger.Warning(e, "加载代理设置失败");
        }
    }

    private sealed record ProxySnapshot(string Mode, string? ManualUrl);
}
