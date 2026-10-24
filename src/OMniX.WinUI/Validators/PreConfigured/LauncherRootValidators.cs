using FluentValidation;
using OMniX.WinUI.ViewModels.SubVms;

namespace OMniX.WinUI.Validators.PreConfigured;

/// <summary>
///     二开：启动器根目录（原 3Dmigoto 文件夹）的校验规则。
///     现在走 XXMI，不再检查 WWMI Loader.exe，改为认 XXMI 结构：
///     根目录标志文件 / 导入器子目录 / 直接就是导入器目录
/// </summary>
public static class LauncherRootValidators
{
    public static IEnumerable<AbstractValidator<PathPicker>> Validators(Func<string?> modelImporterShortNameProvider) =>
        new AbstractValidator<PathPicker>[]
        {
            new IsValidPathFormat(),
            new FolderExists(),
            new IsValidLauncherRootFolder(modelImporterShortNameProvider)
        };

    public static IEnumerable<AbstractValidator<PathPicker>> Validators(string? modelImporterShortName) =>
        Validators(() => modelImporterShortName);
}
