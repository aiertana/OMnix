using FluentValidation;
using OMniX.Core.Contracts.Services;
using PathPicker = OMniX.WinUI.ViewModels.SubVms.PathPicker;

namespace OMniX.WinUI.Validators;

public class IsValidPathFormat : AbstractValidator<PathPicker>
{
    private static readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    public IsValidPathFormat(bool warning = false)
    {
        RuleFor(x => x.Path)
            .Must(path => path is not null && Path.IsPathFullyQualified(path))
            .WithMessage(_localizer.GetLocalizedStringOrDefault("/CharacterManager/Valid_PathFormat", "路径格式不正确")!)
            .WithSeverity(warning ? Severity.Warning : Severity.Error);
    }
}