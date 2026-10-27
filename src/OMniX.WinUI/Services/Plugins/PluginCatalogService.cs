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

    /// <summary>该游戏的插件目录</summary>
    public string PluginsDirectory(string gameId) => _dataRoot.EnsureGameDirectory(gameId, "Plugins");

    /// <summary>
    ///     扫一遍插件目录，每个含 plugin.json 的子目录算一个插件。
    ///     没有 manifest 的旧目录也给列出来（标成「缺少描述文件」），方便玩家自己补。
    /// </summary>
    public IReadOnlyList<PluginManifest> Scan(string gameId)
    {
        var root = PluginsDirectory(gameId);
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

        var root = PluginsDirectory(gameId);
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

    /// <summary>给第三方一个能照着填的模板</summary>
    public string WriteTemplate(string gameId, string pluginId)
    {
        var directory = Path.Combine(PluginsDirectory(gameId), pluginId);
        Directory.CreateDirectory(directory);

        var manifest = PluginManifest.CreateSample(pluginId);
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
