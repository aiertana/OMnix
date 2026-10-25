using FluentValidation;
using OMniX.Core.Contracts.Services;
using OMniX.WinUI.Services.AppManagement;
using OMniX.WinUI.ViewModels.SubVms;

namespace OMniX.WinUI.Validators;

/// <summary>
///     二开：校验所选文件夹是否为可用的启动器根目录（是否能识别到启动器 exe）。
///     找不到只给出警告，因为用户也可以手动填写 Mods 文件夹
/// </summary>
public class ContainsLauncherExecutable : AbstractValidator<PathPicker>
{
    private static readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    public ContainsLauncherExecutable()
    {
        RuleFor(x => x.Path)
            .Must(path => !string.IsNullOrWhiteSpace(path) &&
                          Directory.Exists(path) &&
                          LauncherLocator.FindLauncherExecutable(path) is not null)
            .WithMessage(_localizer.GetLocalizedStringOrDefault("/CharacterManager/Valid_NoLauncherExe", "未在该文件夹中识别到启动器程序（例如 Resources\\Bin\\XXMI Launcher.exe）")!)
            .WithSeverity(Severity.Warning);
    }
}
