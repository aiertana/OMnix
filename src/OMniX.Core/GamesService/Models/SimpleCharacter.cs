using OMniX.Core.GamesService.Interfaces;

namespace OMniX.Core.GamesService.Models;

/// <summary>
///     轻量角色对象：只带 OilFX 界面用到的成员（名称 / 键 / 头像 / ModFilesName）。
///     供独立插件版（OmniX.Plugin.OilFX.UI）从角色资产 JSON 直接构建角色列表用，
///     不承载宿主的皮肤 / 覆盖 / mod 列表逻辑。
/// </summary>
public sealed class SimpleCharacter(
    string internalName,
    string displayName,
    string modFilesName,
    Uri? imageUri,
    ICollection<string> keys,
    int rarity,
    DateTime? releaseDate) : ICharacter
{
    public string DisplayName { get; set; } = displayName;
    public InternalName InternalName { get; init; } = new(internalName);
    public string ModFilesName { get; set; } = modFilesName;
    public bool IsMultiMod { get; set; }
    public ICategory ModCategory => null!;
    public bool IsCustomModObject => false;
    public Uri? ImageUri { get; set; } = imageUri;
    public ICharacter DefaultCharacter => this;
    public IGameClass Class => null!;
    public IGameElement Element => null!;
    public ICollection<string> Keys { get; set; } = keys;
    public ICollection<IRegion> Regions { get; } = [];
    public ICollection<ICharacterSkin> Skins { get; } = [];
    public int Rarity { get; } = rarity;
    public DateTime? ReleaseDate { get; set; } = releaseDate;

    public bool Equals(ICharacter? other) => other is not null && InternalName.Equals(other.InternalName);
    public bool Equals(INameable? other) => other is not null && InternalName.Equals(other.InternalName);
    public bool Equals(IModdableObject? other) => other is not null && InternalName.Equals(other.InternalName);
    public override bool Equals(object? obj) => obj is INameable n && InternalName.Equals(n.InternalName);
    public override int GetHashCode() => InternalName.GetHashCode();
    public override string ToString() => DisplayName;
}
