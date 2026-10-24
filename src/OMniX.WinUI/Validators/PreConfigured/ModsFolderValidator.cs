using FluentValidation;
using PathPicker = OMniX.WinUI.ViewModels.SubVms.PathPicker;

namespace OMniX.WinUI.Validators.PreConfigured;

public static class ModsFolderValidator
{
    public static IEnumerable<AbstractValidator<PathPicker>> Validators => new AbstractValidator<PathPicker>[]
    {
        new IsValidPathFormat(),
        new FolderExists("Folder not found")
    };
}