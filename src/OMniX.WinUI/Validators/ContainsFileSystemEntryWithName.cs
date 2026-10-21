using FluentValidation;
using OMniX.WinUI.ViewModels.SubVms;

namespace OMniX.WinUI.Validators;

public class ContainsFileSystemEntryWithName : AbstractValidator<PathPicker>
{
    public ContainsFileSystemEntryWithName(string filename, string? customMessage = null, bool warning = false)
    {
        filename = filename.ToLower();
        customMessage ??= $"文件夹里找不到：{filename}";
        RuleFor(x => x.Path)
            .Must(path =>
                path is not null &&
                Directory.Exists(path) &&
                Directory.GetFileSystemEntries(path).Any(entry => entry.ToLower().EndsWith(filename))
            )
            .WithMessage(customMessage)
            .WithSeverity(warning ? Severity.Warning : Severity.Error);
    }
}