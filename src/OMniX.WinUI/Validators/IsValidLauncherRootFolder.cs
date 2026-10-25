using FluentValidation;
using OMniX.Core.Contracts.Services;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.ViewModels.SubVms;

namespace OMniX.WinUI.Validators;

/// <summary>
///     二开：校验文件夹是否为可用的启动器根目录（XXMI 结构）。
///     现在通过 XXMI 启动，不再要求 WWMI Loader.exe，改为认：
///     XXMI 标志文件 / 导入器子目录（如 WWMI）/ 直接就是导入器目录。
///     只给警告，因为用户也可以自己填 Mods 文件夹。
/// </summary>
public class IsValidLauncherRootFolder : AbstractValidator<PathPicker>
{
    private static readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    public IsValidLauncherRootFolder(Func<string?> modelImporterShortNameProvider)
    {
        RuleFor(x => x.Path)
            .Must(path =>
                !string.IsNullOrWhiteSpace(path) &&
                Directory.Exists(path) &&
                LauncherLocator.IsValidLauncherRoot(path, modelImporterShortNameProvider()))
            .WithMessage(_ =>
            {
                var importerShortName = modelImporterShortNameProvider();

                return string.IsNullOrWhiteSpace(importerShortName)
                    ? _localizer.GetLocalizedStringOrDefault("/CharacterManager/Valid_NotLauncherRootNoImporter", "这个文件夹看起来不是启动器根目录：找不到 XXMI 标志文件，也没有导入器目录")!
                    : string.Format(_localizer.GetLocalizedStringOrDefault("/CharacterManager/Valid_NotLauncherRoot", "这个文件夹看起来不是启动器根目录：找不到「{0}」子文件夹，也没有 XXMI 标志文件")!, importerShortName);
            })
            .WithSeverity(Severity.Warning);
    }

    public IsValidLauncherRootFolder(string? modelImporterShortName)
        : this(() => modelImporterShortName)
    {
    }
}
