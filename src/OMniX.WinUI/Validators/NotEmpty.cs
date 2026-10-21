using FluentValidation;
using PathPicker = OMniX.WinUI.ViewModels.SubVms.PathPicker;

namespace OMniX.WinUI.Validators;

public class NotEmpty : AbstractValidator<PathPicker>
{
    public NotEmpty(bool warning = false)
    {
        RuleFor(x => x.Path).NotEmpty().WithMessage("文件夹路径不能为空")
            .WithSeverity(warning ? Severity.Warning : Severity.Error);
    }
}