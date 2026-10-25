using OMniX.Core.Contracts.Services;
using OMniX.Core.GamesService;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.Helpers;

namespace OMniX.WinUI.ViewModels.CharacterManagerViewModels.Validation;

public static class Validators
{
    private static readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    public static void AddInternalNameValidators(this FieldValidators<string> validators, ICollection<IModdableObject> allModdableObjects)
    {
        validators.AddRange([
            context => string.IsNullOrWhiteSpace(context.Value.Trim())
                ? new ValidationResult { Message = _localizer.GetLocalizedStringOrDefault("/CharacterManager/Valid_InternalNameRequired", "内部名不能为空") }
                : null,
            context => allModdableObjects.FirstOrDefault(m => m.InternalNameEquals(context.Value)) is { } existingModdableObject
                ? new ValidationResult { Message = string.Format(_localizer.GetLocalizedStringOrDefault("/CharacterManager/Valid_InternalNameTaken", "内部名「{0}」已被 {1} 占用"), context.Value, existingModdableObject.DisplayName) }
                : null,
            context =>
            {
                var invalidFileSystemChars = Path.GetInvalidFileNameChars();


                foreach (var invalidChar in invalidFileSystemChars)
                {
                    if (context.Value.Contains(invalidChar))
                    {
                        return new ValidationResult
                        {
                            Message = string.Format(_localizer.GetLocalizedStringOrDefault("/CharacterManager/Valid_InternalNameInvalidChar", "内部名里有文件名不允许的字符「{0}」"), invalidChar)
                        };
                    }
                }

                return null;
            }
        ]);
    }

    public static void AddModFilesNameValidators(this FieldValidators<string> validators, ICollection<IModdableObject> allModdableObjects)
    {
        validators.Add(context => !context.Value.Trim().IsNullOrEmpty() &&
                                  allModdableObjects.FirstOrDefault(m => m.ModFilesName.Equals(context.Value.Trim(), StringComparison.OrdinalIgnoreCase)) is
                                  { } existingModdableObject
            ? new ValidationResult { Message = string.Format(_localizer.GetLocalizedStringOrDefault("/CharacterManager/Valid_ModFilesNameTaken", "Mod 文件名已被 {0} 使用"), existingModdableObject.DisplayName) }
            : null);
    }

    public static void AddDisplayNameValidators(this FieldValidators<string> validators, ICollection<IModdableObject> allModdableObjects)
    {
        validators.Add(context => allModdableObjects.FirstOrDefault(m => m.DisplayName.Equals(context.Value.Trim(), StringComparison.OrdinalIgnoreCase)) is
        { } existingModdableObject
            ? new ValidationResult
            {
                Message = string.Format(_localizer.GetLocalizedStringOrDefault("/CharacterManager/Valid_DisplayNameConflict", "另一个对象（{0}）已经用了这个显示名，搜索时可能会有冲突"), existingModdableObject.InternalName),
                Type = ValidationType.Warning
            }
            : null);
    }

    public static void AddImageValidators(this FieldValidators<Uri> validators)
    {
        validators.AddRange([
            context => context.Value == null! ? new ValidationResult { Message = _localizer.GetLocalizedStringOrDefault("/CharacterManager/Valid_ImageRequired", "图片不能为空") } : null, context =>
                !context.Value!.IsFile || !File.Exists(context.Value.LocalPath)
                    ? new ValidationResult() { Message = _localizer.GetLocalizedStringOrDefault("/CharacterManager/Valid_ImageFileInvalid", "图片必须是一个存在的有效文件") }
                    : null,
            context =>
            {
                if (!context.Value!.IsFile) return null;
                var fileExtension = Path.GetExtension((string?)context.Value.LocalPath);

                var isSupportedExtension = Constants.SupportedImageExtensions.Contains(fileExtension, StringComparer.OrdinalIgnoreCase);

                return isSupportedExtension
                    ? null
                    : new ValidationResult
                    {
                        Message =
                            string.Format(_localizer.GetLocalizedStringOrDefault("/CharacterManager/Valid_ImageFormatUnsupported", "图片只支持这几种格式：{0}。当前扩展名 {1} 不支持。"), string.Join(", ", Constants.SupportedImageExtensions), fileExtension)
                    };
            }
        ]);
    }

    public static void AddRarityValidators(this List<ValidationCallback<int>> validators)
    {
        validators.AddRange([
            context => context.Value < 0 ? new ValidationResult { Message = _localizer.GetLocalizedStringOrDefault("/CharacterManager/Valid_RarityMin", "稀有度不能小于 0") } : null,
            context => context.Value > 10 ? new ValidationResult { Message = _localizer.GetLocalizedStringOrDefault("/CharacterManager/Valid_RarityMax", "稀有度不能大于 10") } : null
        ]);
    }

    public static void AddElementValidators(this List<ValidationCallback<string>> validators, ICollection<IGameElement> elements)
    {
        validators.Add(context => elements.Any(e => e.InternalNameEquals(context.Value))
            ? null
            : new ValidationResult
            {
                Message = string.Format(_localizer.GetLocalizedStringOrDefault("/CharacterManager/Valid_ElementInvalid", "属性「{0}」不存在。可用值：{1}"), context.Value, string.Join(',', elements.Select(e => e.InternalName)))
            });
    }

    public static void AddKeysValidators(this FieldValidators<IReadOnlyCollection<string>> validators, ICollection<ICharacter> allCharacters)
    {
        validators.Add(context =>
                {
                    var newKeys = context.Value;
                    if (newKeys.Count == 0) return null;


                    ValueTuple<ICharacter, string>? duplicateKey = null;
                    foreach (var character in allCharacters)
                    {
                        var duplicate = newKeys.FirstOrDefault(k => character.Keys.Contains(k, StringComparer.OrdinalIgnoreCase));
                        if (duplicate is not null)
                        {
                            duplicateKey = (character, duplicate);
                            break;
                        }
                    }

                    if (duplicateKey is null) return null;

                    return new ValidationResult
                    {
                        Message = string.Format(_localizer.GetLocalizedStringOrDefault("/CharacterManager/Valid_KeyTaken", "关键字「{0}」已被 {1} 使用"), duplicateKey.Value.Item2, duplicateKey.Value.Item1.DisplayName)
                    };
                });
    }
}
