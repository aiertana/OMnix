using System.IO.Compression;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.Helpers;

namespace OMniX.WinUI.Services.Plugins;

/// <summary>
///     功能插件目录。
///     做的事就一件：扫目录 → 认 plugin.json → 列出来。
///     谁写的插件都行，只要按约定丢进目录里。
/// </summary>
public sealed class PluginCatalogService
{
    private const string ManifestFileName = "plugin.json";

    private readonly GameDataRootService _dataRoot;

    public PluginCatalogService(GameDataRootService dataRoot)
    {
        _dataRoot = dataRoot;
    }

    /// <summary>该游戏的插件目录（旧版布局，只用来迁移）</summary>
    public string PluginsDirectory(string gameId) => _dataRoot.EnsureGameDirectory(gameId, "Plugins");

    /// <summary>
    ///     插件统一放这儿：GamesData\Plugins，全局一份。
    ///     插件经常是好几个游戏通用的（XXMI 那套），按游戏各存一份就得复制好几遍；
    ///     适用于哪个游戏由 plugin.json 的 games 字段说了，不靠放的位置。
    /// </summary>
    public string PluginsDirectory()
    {
        var path = Path.Combine(_dataRoot.GamesDataRoot, "Plugins");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    ///     旧版把插件放 GamesData\&lt;游戏&gt;\Plugins，现在统一搬到全局目录。
    ///     只搬带 plugin.json 的（那才是插件），同名的不覆盖。幂等，随时调都行。
    /// </summary>
    public void EnsureMigrated()
    {
        foreach (var game in GameCatalog.All)
        {
            var legacy = Path.Combine(_dataRoot.GameRoot(game.Id), "Plugins");

            if (!Directory.Exists(legacy))
                continue;

            foreach (var directory in Directory.EnumerateDirectories(legacy))
            {
                if (!File.Exists(Path.Combine(directory, ManifestFileName)))
                    continue;

                var target = Path.Combine(PluginsDirectory(), Path.GetFileName(directory));

                if (Directory.Exists(target))
                    continue; // 全局已有同名，不动旧的

                Directory.Move(directory, target);
            }
        }
    }

    /// <summary>
    ///     扫一遍插件目录，每个含 plugin.json 的子目录算一个插件。
    ///     没有 manifest 的旧目录也给列出来（标成「缺少描述文件」），方便玩家自己补。
    ///     只列当前游戏能用的：games 留空算通用，写了就按声明过滤。
    /// </summary>
    public IReadOnlyList<PluginManifest> Scan(string gameId) => ScanCore(gameId);

    /// <summary>全量扫描，不过滤游戏 —— 「全局」视图用：所有插件都在，各自标适用范围。</summary>
    public IReadOnlyList<PluginManifest> ScanAll() => ScanCore(null);

    private IReadOnlyList<PluginManifest> ScanCore(string? gameId)
    {
        var root = PluginsDirectory();
        var found = new List<PluginManifest>();

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var manifestPath = Path.Combine(directory, ManifestFileName);
            PluginManifest? manifest = null;

            if (File.Exists(manifestPath))
            {
                manifest = PluginManifest.FromJson(File.ReadAllText(manifestPath));
                if (manifest is not null)
                    manifest.ManifestPath = manifestPath;
            }

            if (manifest is null)
            {
                manifest = new PluginManifest
                {
                    Id = Path.GetFileName(directory),
                    Name = Path.GetFileName(directory),
                    Description = "目录里没有 plugin.json，管理器只能认出文件夹名",
                    Type = "link"
                };
            }

            // 不适用于当前游戏的插件不显示（文件还在，换个游戏就能看到）
            if (gameId is not null && !manifest.SupportsGame(gameId))
                continue;

            if (string.IsNullOrWhiteSpace(manifest.Id))
                manifest.Id = Path.GetFileName(directory);

            if (string.IsNullOrWhiteSpace(manifest.Name))
                manifest.Name = manifest.Id;

            manifest.DirectoryPath = directory;
            found.Add(manifest);
        }

        return found.OrderBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>开关：把目标文件改名加/去 .disabled 后缀（和 Mod 开关一个路数）</summary>
    public bool SetEnabled(PluginManifest plugin, bool enabled)
    {
        var touched = false;

        foreach (var target in plugin.TargetFiles())
        {
            var disabled = target + ".disabled";

            if (enabled)
            {
                if (File.Exists(disabled) && !File.Exists(target))
                {
                    File.Move(disabled, target);
                    touched = true;
                }
            }
            else
            {
                if (File.Exists(target) && !File.Exists(disabled))
                {
                    File.Move(target, disabled);
                    touched = true;
                }
            }
        }

        plugin.Enabled = enabled;
        Save(plugin);
        return touched;
    }

    public void Save(PluginManifest plugin)
    {
        if (string.IsNullOrWhiteSpace(plugin.DirectoryPath))
            return;

        Directory.CreateDirectory(plugin.DirectoryPath);

        var path = string.IsNullOrWhiteSpace(plugin.ManifestPath)
            ? Path.Combine(plugin.DirectoryPath, ManifestFileName)
            : plugin.ManifestPath;

        File.WriteAllText(path, plugin.ToJson());
        plugin.ManifestPath = path;
    }

    /// <summary>
    ///     导入离线包。zip 里带 plugin.json 就认：
    ///     根目录有 manifest → 整包当一个插件；否则每个含 manifest 的子目录各算一个。
    ///     考虑到不是所有玩家都能连 GitHub，这条路必须通。
    /// </summary>
    public IReadOnlyList<PluginManifest> ImportOfflinePackage(string zipPath, string gameId)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("离线包不存在", zipPath);

        var root = PluginsDirectory();
        var imported = new List<PluginManifest>();
        var staging = Path.Combine(Path.GetTempPath(), "OMniX_plugin_" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(staging);

        try
        {
            ZipFile.ExtractToDirectory(zipPath, staging, true);

            var entries = new List<string>();

            if (File.Exists(Path.Combine(staging, ManifestFileName)))
            {
                entries.Add(staging);
            }
            else
            {
                entries.AddRange(Directory.EnumerateDirectories(staging)
                    .Where(directory => File.Exists(Path.Combine(directory, ManifestFileName))));

                // 有些包会多套一层目录，往下再看一层
                if (entries.Count == 0)
                {
                    foreach (var directory in Directory.EnumerateDirectories(staging))
                    {
                        entries.AddRange(Directory.EnumerateDirectories(directory)
                            .Where(inner => File.Exists(Path.Combine(inner, ManifestFileName))));
                    }
                }
            }

            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var target = Path.Combine(root, name);
                CopyDirectory(entry, target);
                imported.Add(Scan(gameId).FirstOrDefault(plugin =>
                                   string.Equals(plugin.Id, name, StringComparison.OrdinalIgnoreCase)) ??
                               new PluginManifest { Id = name, Name = name, DirectoryPath = target });
            }
        }
        finally
        {
            try
            {
                Directory.Delete(staging, true);
            }
            catch
            {
                // 临时目录删不掉无所谓
            }
        }

        return imported;
    }

    /// <summary>给第三方一个能照着填的模板。默认只适用于生成它的那个游戏，自己再改 games。</summary>
    public string WriteTemplate(string gameId, string pluginId)
    {
        var directory = Path.Combine(PluginsDirectory(), pluginId);
        Directory.CreateDirectory(directory);

        var manifest = PluginManifest.CreateSample(pluginId);
        manifest.Games = [gameId];
        manifest.DirectoryPath = directory;
        Save(manifest);

        return directory;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);

        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
}
