using FluentValidation;
using OMniX.Core.Contracts.Services;
using PathPicker = OMniX.WinUI.ViewModels.SubVms.PathPicker;

namespace OMniX.WinUI.Validators;

public class FileExists : AbstractValidator<PathPicker>
{
    private static readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    public FileExists(string? message = null, bool warning = false)
    {
        message ??= _localizer.GetLocalizedStringOrDefault("/CharacterManager/Valid_FileExistsDefault", "文件不存在");
        RuleFor(x => x.Path).Must(File.Exists)
            .WithMessage(message)
            .WithSeverity(warning ? Severity.Warning : Severity.Error);
    }
}