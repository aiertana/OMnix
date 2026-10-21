using FluentValidation;
using OMniX.WinUI.ViewModels.SubVms;

namespace OMniX.WinUI.Validators.PreConfigured;

public static class UnloadedModsValidators
{
    public static IEnumerable<AbstractValidator<PathPicker>> Validators => new AbstractValidator<PathPicker>[]
    {
        new IsValidPathFormat(),
        new FolderExists("Folder does not exist and will be created", true)
    };
}