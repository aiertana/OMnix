using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OMniX.Core.Contracts.Services;
using OMniX.Core.GamesService;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.GamesService.Models;
using OMniX.Core.Helpers;
using OMniX.WinUI.Contracts.ViewModels;
using OMniX.WinUI.Services;

namespace OMniX.WinUI.ViewModels;

public partial class CharacterManagerViewModel : ObservableRecipient, INavigationAware
{
    private readonly IGameService _gameService;
    private readonly ImageHandlerService _imageHandlerService;
    private readonly ILanguageLocalizer _localizer;

    [ObservableProperty] private ICharacter? _selectedCharacter;
    [ObservableProperty] private string _searchBoxText;

    private List<ICharacter> _characters = new();

    private static InternalName? _lastSelectedCharacter;

    public ObservableCollection<CharacterSearchResult> Suggestions { get; } = new();

    /// <summary>
    ///     二开：所有角色的缩略图。没选角色时铺在页面上，点一下直接进编辑页。
    /// </summary>
    public ObservableCollection<CharacterSearchResult> AllCharacters { get; } = new();

    /// <summary>
    ///     二开：当前没选任何角色（这时候显示头像墙）。
    /// </summary>
    public bool IsNoCharacterSelected => SelectedCharacter is null;

    /// <summary>
    ///     二开：当前选了角色（这时候显示编辑页）。
    /// </summary>
    public bool HasSelectedCharacter => SelectedCharacter is not null;

    public event EventHandler<SetSelectionArgs>? SetSelection;

    public CharacterManagerViewModel(IGameService gameService,
        ImageHandlerService imageHandlerService, ILanguageLocalizer localizer)
    {
        _gameService = gameService;
        _imageHandlerService = imageHandlerService;
        _localizer = localizer;
    }


    private void SetCharacter(ICharacter character)
    {
        SelectedCharacter = character;

        SetSelection?.Invoke(this, new SetSelectionArgs(character));
        _lastSelectedCharacter = character.InternalName;
    }

    private void ResetCharacter()
    {
        SetSelection?.Invoke(this, new SetSelectionArgs(null));
        _lastSelectedCharacter = null;
    }

    [RelayCommand]
    private void SelectCharacter(CharacterSearchResult? selectedCharacter)
    {
        if (selectedCharacter is null)
        {
            return;
        }

        var character = _characters.FirstOrDefault(c => c.InternalNameEquals(selectedCharacter.InternalName));

        if (character is null)
        {
            OnSearchTextChanged("");
            return;
        }

        ResetCharacter();
        SetCharacter(character);
        Suggestions.Clear();
    }


    /// <summary>
    ///     二开：点了头像墙里的某一个 → 直接打开它的编辑页。
    /// </summary>
    [RelayCommand]
    private void OpenCharacter(CharacterSearchResult? selectedCharacter)
    {
        if (selectedCharacter is null)
            return;

        SelectCharacter(selectedCharacter);
    }

    /// <summary>
    ///     二开：从编辑页返回头像墙。
    /// </summary>
    [RelayCommand]
    private void BackToGrid()
    {
        Suggestions.Clear();
        SearchBoxText = string.Empty;

        // 先清掉选中项，界面会切回头像墙；再通知外层把编辑页内容清空
        SelectedCharacter = null;
        ResetCharacter();
    }

    /// <summary>
    ///     二开：选中角色变了要通知界面切换「头像墙 / 编辑页」。
    /// </summary>
    partial void OnSelectedCharacterChanged(ICharacter? value)
    {
        OnPropertyChanged(nameof(IsNoCharacterSelected));
        OnPropertyChanged(nameof(HasSelectedCharacter));
    }

    [RelayCommand]
    private void OpenCreateCharacterForm()
    {
        ResetCharacter();
        Suggestions.Clear();
        _lastSelectedCharacter = null;
        SearchBoxText = string.Empty;
        SetSelection?.Invoke(this, new SetSelectionArgs(true));
    }

    public void OnNavigatedTo(object parameter)
    {
        _characters = _gameService.GetCharacters().Concat(_gameService.GetDisabledCharacters()).ToList();

        // 二开：把全部角色做成缩略图，没选角色时铺在页面上
        AllCharacters.Clear();

        foreach (var c in _characters)
            AllCharacters.Add(CharacterSearchResult.FromCharacter(c, _imageHandlerService.PlaceholderImagePath));

        ICharacter? character = null;
        var internalName = (parameter as InternalName)?.Id ?? parameter as string ?? (parameter as ICharacter)?.InternalName.Id;
        if (!internalName.IsNullOrEmpty())
        {
            character = _characters.FirstOrDefault(c => c.InternalNameEquals(internalName));
            if (character is not null)
            {
                SetCharacter(character);
                return;
            }
        }


        if (_lastSelectedCharacter is null) return;
        character = _characters.FirstOrDefault(c => c.InternalNameEquals(_lastSelectedCharacter));
        if (character is null) return;
        SetCharacter(character);
    }

    public void OnNavigatedFrom()
    {
    }


    public void OnSearchTextChanged(string query)
    {
        Suggestions.Clear();
        if (string.IsNullOrWhiteSpace(query))
        {
            ResetCharacter();
            return;
        }


        var suitableCharacters = _gameService.QueryCharacters(query, minScore: 100, includeDisabledCharacters: true)
            .OrderByDescending(kv => kv.Value)
            .ToArray();

        if (suitableCharacters.Length == 0)
        {
            Suggestions.Add(new CharacterSearchResult
            {
                Name = _localizer.GetLocalizedStringOrDefault("/CharacterManager/CharMgr_NoMatchResult", "没有找到匹配的角色"),
                ImagePath = _imageHandlerService.PlaceholderImagePath
            });
            return;
        }

        foreach (var character in suitableCharacters.Select(kv => kv.Key))
            Suggestions.Add(CharacterSearchResult.FromCharacter(character, _imageHandlerService.PlaceholderImagePath));
    }


    public class SetSelectionArgs : EventArgs
    {
        public INameable? Character { get; }

        public bool NewCharacter { get; }

        public SetSelectionArgs(INameable? character)
        {
            Character = character;
        }

        public SetSelectionArgs(bool newCharacter)
        {
            NewCharacter = newCharacter;
        }
    }
}

public class CharacterSearchResult
{
    public string InternalName { get; init; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;

    public static CharacterSearchResult FromCharacter(ICharacter character, string placeHolderImage)
    {
        return new CharacterSearchResult
        {
            InternalName = character.InternalName,
            Name = character.DisplayName,
            ImagePath = character.ImageUri?.ToString() ?? placeHolderImage
        };
    }

    public override string ToString()
    {
        return Name;
    }
}