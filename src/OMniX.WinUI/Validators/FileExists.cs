using FluentValidation;
using PathPicker = OMniX.WinUI.ViewModels.SubVms.PathPicker;

namespace OMniX.WinUI.Validators;

public class FileExists : AbstractValidator<PathPicker>
{
    public FileExists(string message = "文件不存在", bool warning = false)
    {
        RuleFor(x => x.Path).Must(File.Exists)
            .WithMessage(message)
            .WithSeverity(warning ? Severity.Warning : Severity.Error);
    }
}