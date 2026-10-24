using FluentValidation;
using PathPicker = OMniX.WinUI.ViewModels.SubVms.PathPicker;

namespace OMniX.WinUI.Validators;

public class FolderExists : AbstractValidator<PathPicker>
{
    public FolderExists(string message = "文件夹不存在", bool warning = false)
    {
        RuleFor(x => x.Path).Must(Directory.Exists)
            .WithMessage(message)
            .WithSeverity(warning ? Severity.Warning : Severity.Error);
    }
}