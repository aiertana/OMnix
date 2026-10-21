using OMniX.Core.GamesService;
using OMniX.Core.GamesService.Interfaces;
using OMniX.Core.Helpers;

namespace OMniX.WinUI.ViewModels.CharacterManagerViewModels.Validation;

public static class Validators
{
    public static void AddInternalNameValidators(this FieldValidators<string> validators, ICollection<IModdableObject> allModdableObjects)
    {
        validators.AddRange([
            context => string.IsNullOrWhiteSpace(context.Value.Trim())
                ? new ValidationResult { Message = "内部名不能为空" }
                : null,
            context => allModdableObjects.FirstOrDefault(m => m.InternalNameEquals(context.Value)) is { } existingModdableObject
                ? new ValidationResult { Message = $"内部名「{context.Value}」已被 {existingModdableObject.DisplayName} 占用" }
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
                            Message = $"内部名里有文件名不允许的字符「{invalidChar}」"
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
            ? new ValidationResult { Message = $"Mod 文件名已被 {existingModdableObject.DisplayName} 使用" }
            : null);
    }

    public static void AddDisplayNameValidators(this FieldValidators<string> validators, ICollection<IModdableObject> allModdableObjects)
    {
        validators.Add(context => allModdableObjects.FirstOrDefault(m => m.DisplayName.Equals(context.Value.Trim(), StringComparison.OrdinalIgnoreCase)) is
        { } existingModdableObject
            ? new ValidationResult
            {
                Message = $"另一个对象（{existingModdableObject.InternalName}）已经用了这个显示名，搜索时可能会有冲突",
                Type = ValidationType.Warning
            }
            : null);
    }

    public static void AddImageValidators(this FieldValidators<Uri> validators)
    {
        validators.AddRange([
            context => context.Value == null! ? new ValidationResult { Message = "图片不能为空" } : null, context =>
                !context.Value!.IsFile || !File.Exists(context.Value.LocalPath)
                    ? new ValidationResult() { Message = "图片必须是一个存在的有效文件" }
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
                            $"图片只支持这几种格式：{string.Join(", ", Constants.SupportedImageExtensions)}。当前扩展名 {fileExtension} 不支持。"
                    };
            }
        ]);
    }

    public static void AddRarityValidators(this List<ValidationCallback<int>> validators)
    {
        validators.AddRange([
            context => context.Value < 0 ? new ValidationResult { Message = "稀有度不能小于 0" } : null,
            context => context.Value > 10 ? new ValidationResult { Message = "稀有度不能大于 10" } : null
        ]);
    }

    public static void AddElementValidators(this List<ValidationCallback<string>> validators, ICollection<IGameElement> elements)
    {
        validators.Add(context => elements.Any(e => e.InternalNameEquals(context.Value))
            ? null
            : new ValidationResult
            {
                Message = $"属性「{context.Value}」不存在。可用值：{string.Join(',', elements.Select(e => e.InternalName))}"
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
                        Message = $"关键字「{duplicateKey.Value.Item2}」已被 {duplicateKey.Value.Item1.DisplayName} 使用"
                    };
                });
    }
}
