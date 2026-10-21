using OMniX.Core.Contracts.Entities;
using OMniX.Core.Helpers;

namespace OMniX.Core.Entities.Mods.SkinMod;

public class SkinMod : Mod, ISkinMod
{
    private string _modIniPath = string.Empty;
    private readonly string _configFileName = Constants.ModConfigFileName;
    private string _configFilePath = string.Empty;

    public Guid Id { get; private set; }

    public SkinModSettingsManager Settings { get; private set; } = null!;
    public SkinModKeySwapManager? KeySwaps { get; private set; }


    public string GetDisplayName()
    {
        var displayName = Settings.TryGetSettings(out var settings) && !settings.CustomName.IsNullOrEmpty()
            ? settings.CustomName
            : GetNameWithoutDisabledPrefix();

        return displayName;
    }

    public void ClearCache()
    {
        Settings.ClearSettings();
        KeySwaps?.ClearKeySwaps();
    }


    private SkinMod(DirectoryInfo modDirectory) : base(modDirectory)
    {
        Init();
    }


    public static Task<ISkinMod> CreateModAsync(string fullPath, bool forceGenerateNewId = false)
    {
        if (!Path.IsPathFullyQualified(fullPath))
            throw new ArgumentException("Path must be absolute.", nameof(fullPath));

        var modDirectory = new DirectoryInfo(fullPath);

        return CreateModAsync(modDirectory, forceGenerateNewId);
    }

    public static async Task<ISkinMod> CreateModAsync(DirectoryInfo modFolder, bool forceGenerateNewId = false)
    {
        if (!modFolder.Exists)
            throw new DirectoryNotFoundException($"Directory not found at path: {modFolder.FullName}");


        var skinMod = new SkinMod(modFolder);
        skinMod.Settings = new SkinModSettingsManager(skinMod);

        skinMod.Id = await skinMod.Settings.InitializeAsync().ConfigureAwait(false);

        await skinMod.GetModIniPathAsync().ConfigureAwait(false);

        if (!forceGenerateNewId) return skinMod;

        // TODO: Redo cached settings, as the way it's done now is a bit of a mess.
        // settings was lazy loaded before, but now it's not. This leads to unnecessary complexity and code checks.
        var settings = await skinMod.Settings.ReadSettingsAsync().ConfigureAwait(false);
        settings.Id = Guid.NewGuid();
        skinMod.Id = settings.Id;
        await skinMod.Settings.SaveSettingsAsync(settings).ConfigureAwait(false);


        return skinMod;
    }

    private void Init()
    {
        var modFolderAttributes = File.GetAttributes(_modDirectory.FullName);
        if (!modFolderAttributes.HasFlag(FileAttributes.Directory))
            throw new ArgumentException("Mod must be a folder.", nameof(_modDirectory.FullName));
        Refresh();
    }

    private void Refresh()
    {
        _modDirectory.Refresh();
        _configFilePath = Path.Combine(FullPath, _configFileName);
        _modIniPath = Path.Combine(FullPath, _modIniPath);
    }

    public bool ContainsOnlyOmniXFiles()
    {
        return _modDirectory.EnumerateFiles()
            .All(file => Constants.IsOmniXFile(file.Name));
    }

    private static string? HasMergedInIFile(DirectoryInfo modDirectory)
    {
        // 先看根目录
        var mergedIniPath = modDirectory.EnumerateFiles("*.ini", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(iniFiles =>
                Constants.ScriptIniNames.Any(iniNames =>
                    iniNames.Equals(iniFiles.Name, StringComparison.OrdinalIgnoreCase)))
            ?.FullName;

        if (mergedIniPath is not null)
            return mergedIniPath;

        // 二开：很多 Mod 的 ini 放在子文件夹里（比如 尤诺-蓝宝石月亮\[DaisyMeow]xxx\mod.ini）
        // 这里递归找一层，优先选层级最浅、名字最标准的那个
        try
        {
            var nested = modDirectory
                .EnumerateFiles("*.ini", SearchOption.AllDirectories)
                .Where(file => Constants.ScriptIniNames.Any(iniNames =>
                    iniNames.Equals(file.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                .OrderBy(file => file.FullName.Count(c => c == Path.DirectorySeparatorChar))
                .ThenBy(file => Constants.ScriptIniNames.ToList()
                    .FindIndex(name => name.Equals(file.Name, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault();

            return nested?.FullName;
        }
        catch (Exception)
        {
            // 目录权限之类的问题不影响主流程
            return null;
        }
    }

    public override ISkinMod CopyTo(string absPath)
    {
        var newModFolder = base.CopyTo(absPath);
        return CreateModAsync(newModFolder.FullPath).GetAwaiter().GetResult();
    }

    public async Task<string?> GetModIniPathAsync()
    {
        var settings = await Settings.ReadSettingsAsync(useCache: true).ConfigureAwait(false);

        var modIniPath = settings.MergedIniPath?.LocalPath;

        // Empty string means that the user has disabled key swaps for this mod.
        // Or that OMniX should not touch the merged.ini file.
        if (modIniPath == string.Empty)
        {
            KeySwaps = null;
            return null;
        }

        // 二开：配置里存的是相对 Mod 目录的路径（而且带 URL 编码，%20 之类），
        // 之前直接拿这个相对路径去 File.Exists，是拿进程工作目录拼的，永远找不到，
        // 于是嵌套目录的 Mod 一直读不到 ini → 没有变体。先补齐成绝对路径。
        if (modIniPath is not null && !Path.IsPathFullyQualified(modIniPath))
        {
            modIniPath = Path.GetFullPath(
                Path.Combine(_modDirectory.FullName, modIniPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        if (modIniPath is not null && File.Exists(modIniPath))
        {
            KeySwaps ??= new SkinModKeySwapManager(this);
            return modIniPath;
        }

        // 二开：缓存失效时重新扫一遍目录，而不是清掉缓存就放弃。
        // ini 可能埋在子目录里（比如 Mod\同名\Exce - xxx\mod.ini）。
        var rescannedPath = HasMergedInIFile(_modDirectory);

        if (rescannedPath is null)
        {
            KeySwaps = null;

            if (settings.MergedIniPath is not null)
            {
                settings.MergedIniPath = null;
                await Settings.SaveSettingsAsync(settings).ConfigureAwait(false);
            }

            return null;
        }

        settings.MergedIniPath = new Uri(rescannedPath);
        await Settings.SaveSettingsAsync(settings).ConfigureAwait(false);

        KeySwaps ??= new SkinModKeySwapManager(this);

        return rescannedPath;
    }

    public string GetNameWithoutDisabledPrefix() =>
        ModFolderHelpers.GetFolderNameWithoutDisabledPrefix(_modDirectory.Name);

    public static bool operator ==(SkinMod? left, SkinMod? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (ReferenceEquals(left, null)) return false;
        if (ReferenceEquals(right, null)) return false;
        return left.Id.Equals(right.Id);
    }

    public static bool operator !=(SkinMod? left, SkinMod? right)
    {
        return !(left == right);
    }

    public bool Equals(ISkinMod? x, ISkinMod? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (ReferenceEquals(x, null)) return false;
        if (ReferenceEquals(y, null)) return false;
        return x.Id.Equals(y.Id);
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals(this, (SkinMod)obj);
    }

    public bool Equals(ISkinMod? other)
    {
        return Equals(this, other);
    }

    public override int GetHashCode()
    {
        // ReSharper disable once NonReadonlyMemberInGetHashCode
        return Id.GetHashCode();
    }

    public int GetHashCode(ISkinMod obj)
    {
        return obj.Id.GetHashCode();
    }

    public override string ToString()
    {
        return GetDisplayName();
    }
}