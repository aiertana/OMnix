using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OMniX.Core.Contracts.Services;
using OMniX.Core.GamesService.Interfaces;
using OMniX.WinUI.Services.Notifications;

namespace OMniX.WinUI.Models;

public partial class CharacterGridItemModel : ObservableObject, IEquatable<CharacterGridItemModel>,
    IEquatable<IModdableObject>
{
    [ObservableProperty] private IModdableObject _character;
    [ObservableProperty] private Uri _imageUri;

    /// <summary>
    ///     二开：刚新建的角色。用在列表里临时置顶，并在卡片右上角显示「新」角标。
    /// </summary>
    [ObservableProperty] private bool _isNew;

    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private bool _warning;
    [ObservableProperty] private bool _isHidden;

    [ObservableProperty] private bool _notification;
    [ObservableProperty] private AttentionType _notificationType;

    [ObservableProperty] private int _modCount;

    [ObservableProperty] private string _modCountString = string.Empty;

    [ObservableProperty] private bool _hasMods;
    [ObservableProperty] private bool _hasEnabledMods;

    public ObservableCollection<CharacterModItem> Mods { get; } = new();

    /// <summary>
    ///     二开：这一项是网格里的“新增角色”占位卡，不对应真实角色
    /// </summary>
    public bool IsAddCard { get; init; }

    public bool IsNotAddCard => !IsAddCard;

    /// <summary>
    ///     二开：造一个“新增角色”占位项。
    ///     fallback 只是一个绑定回退（模板里角色卡内容被隐藏，不会显示它的数据），
    ///     传 null 会让 XAML 绑定取 Character 时崩溃，所以必须传一个真实角色。
    /// </summary>
    public static CharacterGridItemModel CreateAddCard(IModdableObject fallback) =>
        new(fallback) { IsAddCard = true };

    public CharacterGridItemModel(IModdableObject character)
    {
        Character = character;
        ImageUri = character?.ImageUri ?? ModModel.PlaceholderImagePath;
    }

    public void SetMods(IEnumerable<CharacterModItem> mods)
    {
        Mods.Clear();
        var enabledMods = 0;
        foreach (var mod in mods)
        {
            Mods.Add(mod);
            if (mod.IsEnabled)
                enabledMods++;
        }

        ModCount = Mods.Count;
        HasMods = ModCount > 0;
        HasEnabledMods = enabledMods > 0;

        if (HasEnabledMods)
            ModCountString = enabledMods == ModCount ? enabledMods.ToString() : $"{enabledMods} / {ModCount}";
        else
            ModCountString = ModCount.ToString();
    }

    public bool Equals(CharacterGridItemModel? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;

        // 二开：占位卡只和自己相等，避免和真实角色混淆
        if (IsAddCard || other.IsAddCard)
            return IsAddCard == other.IsAddCard;

        return Character.Equals(other.Character);
    }

    public bool Equals(IModdableObject? other)
    {
        if (IsAddCard || ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(Character, other)) return true;
        return Character.InternalNameEquals(other.InternalName);
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is CharacterGridItemModel other && Equals(other);
    }

    public override int GetHashCode() => IsAddCard ? 0 : Character.GetHashCode();

    public static bool operator ==(CharacterGridItemModel? left, CharacterGridItemModel? right) => Equals(left, right);

    public static bool operator !=(CharacterGridItemModel? left, CharacterGridItemModel? right) => !Equals(left, right);


    public override string ToString()
    {
        return IsAddCard
            ? App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("/Characters/AddCardToString", "新增角色")
            : Character.DisplayName;
    }
}

public record CharacterModItem(string Name, bool IsEnabled, DateTime DateAdded);