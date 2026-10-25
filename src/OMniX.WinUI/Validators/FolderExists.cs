using FluentValidation;
using OMniX.Core.Contracts.Services;
using PathPicker = OMniX.WinUI.ViewModels.SubVms.PathPicker;

namespace OMniX.WinUI.Validators;

public class FolderExists : AbstractValidator<PathPicker>
{
    private static readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    public FolderExists(string? message = null, bool warning = false)
    {
        message ??= _localizer.GetLocalizedStringOrDefault("/CharacterManager/Valid_FolderExistsDefault", "文件夹不存在");
        RuleFor(x => x.Path).Must(Directory.Exists)
            .WithMessage(message)
            .WithSeverity(warning ? Severity.Warning : Severity.Error);
    }
}