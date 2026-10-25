using OMniX.Core.Contracts.Services;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.Helpers;
using OMniX.WinUI.Services;
using OMniX.WinUI.ViewModels.CharacterManagerViewModels.Validation;

namespace OMniX.WinUI.ViewModels.CharacterManagerViewModels;

public sealed partial class EditCharacterForm : Form
{
    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    public EditCharacterForm()
    {
    }

    public void Initialize(ICharacter character, ICollection<IModdableObject> allModdableObjects)
    {
        allModdableObjects = allModdableObjects.Contains(character)
            ? allModdableObjects.Where(mo => !mo.Equals(character)).ToArray()
            : allModdableObjects;

        InternalName.ValidationRules.AddInternalNameValidators(allModdableObjects);
        InternalName.ReInitializeInput(character.InternalName);

        DisplayName.ValidationRules.AddDisplayNameValidators(allModdableObjects);
        DisplayName.ValidationRules.Add(context =>
            context.Value.Trim().IsNullOrEmpty() ? new ValidationResult { Message = _localizer.GetLocalizedStringOrDefault("/CharacterManager/Valid_DisplayNameRequired", "显示名称不能为空") } : null);
        DisplayName.ReInitializeInput(character.DisplayName);

        Image.ValidationRules.AddImageValidators();
        Image.ReInitializeInput(character.ImageUri ?? ImageHandlerService.StaticPlaceholderImageUri);

        Keys.ValidationRules.AddKeysValidators(allModdableObjects.OfType<ICharacter>().ToList());
        Keys.ReInitializeInput(character.Keys);

        IsMultiMod.ReInitializeInput(character.IsMultiMod);
        IsInitialized = true;
    }

    public InputField<Uri> Image { get; } = new(ImageHandlerService.StaticPlaceholderImageUri);
    public StringInputField InternalName { get; } = new(string.Empty);
    public StringInputField DisplayName { get; } = new(string.Empty);
    public ListInputField<string> Keys { get; } = new();

    public InputField<bool> IsMultiMod { get; } = new(false);
}