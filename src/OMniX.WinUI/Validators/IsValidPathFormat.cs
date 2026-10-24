using FluentValidation;
using PathPicker = OMniX.WinUI.ViewModels.SubVms.PathPicker;

namespace OMniX.WinUI.Validators;

public class IsValidPathFormat : AbstractValidator<PathPicker>
{
    public IsValidPathFormat(bool warning = false)
    {
        RuleFor(x => x.Path)
            .Must(path => path is not null && Path.IsPathFullyQualified(path))
            .WithMessage("路径格式不正确")
            .WithSeverity(warning ? Severity.Warning : Severity.Error);
    }
}