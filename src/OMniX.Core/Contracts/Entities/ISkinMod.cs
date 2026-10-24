using OMniX.Core.Entities.Mods.SkinMod;

namespace OMniX.Core.Contracts.Entities;

public interface ISkinMod : IMod, IEqualityComparer<ISkinMod>, IEquatable<ISkinMod>
{
    Guid Id { get; }
    public string GetDisplayName();
    public void ClearCache();
    public SkinModSettingsManager Settings { get; }
    public SkinModKeySwapManager? KeySwaps { get; }
    public new ISkinMod CopyTo(string absPath);

    public bool ContainsOnlyOmniXFiles();
    public Task<string?> GetModIniPathAsync();

    // Get folder name without the disabled prefix
    public string GetNameWithoutDisabledPrefix();
}