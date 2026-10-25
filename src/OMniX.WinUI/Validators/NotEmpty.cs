using FluentValidation;
using OMniX.Core.Contracts.Services;
using PathPicker = OMniX.WinUI.ViewModels.SubVms.PathPicker;

namespace OMniX.WinUI.Validators;

public class NotEmpty : AbstractValidator<PathPicker>
{
    private static readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    public NotEmpty(bool warning = false)
    {
        RuleFor(x => x.Path).NotEmpty().WithMessage(_localizer.GetLocalizedStringOrDefault("/CharacterManager/Valid_NotEmptyPath", "文件夹路径不能为空")!)
            .WithSeverity(warning ? Severity.Warning : Severity.Error);
    }
}