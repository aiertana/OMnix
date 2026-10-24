using FluentValidation;
using OMniX.WinUI.ViewModels.SubVms;

namespace OMniX.WinUI.Validators.PreConfigured;

public static class GimiFolderRootValidators
{
    public static ICollection<AbstractValidator<PathPicker>> Validators(IEnumerable<string> validMiExeFilenames)
    {
        return new AbstractValidator<PathPicker>[]
        {
            new IsValidPathFormat(),
            new FolderExists(),
            new ContainsAnyFileSystemEntryWithNames(validMiExeFilenames, warning: true)
        };
    }
}