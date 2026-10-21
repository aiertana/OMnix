using FluentValidation;
using OMniX.WinUI.ViewModels.SubVms;

namespace OMniX.WinUI.Validators;

public class ContainsAnyFileSystemEntryWithNames : AbstractValidator<PathPicker>
{
    public ContainsAnyFileSystemEntryWithNames(IEnumerable<string> filenames, string? customMessage = null, bool warning = false)
    {
        var fileNamesArray = filenames.ToArray();
        var filenamesLowerArray = fileNamesArray.Select(name => name.ToLower()).ToArray();

        customMessage ??=
            $"文件夹里找不到以下名称的文件或文件夹：{string.Join(" 或 ", fileNamesArray)}";

        RuleFor(x => x.Path)
            .Must(path =>
                path is not null &&
                Directory.Exists(path) &&
                Directory.GetFileSystemEntries(path)
                    .Any(entry => filenamesLowerArray.Any(name => entry.ToLower().EndsWith(name)))
            )
            .WithMessage(customMessage)
            .WithSeverity(warning ? Severity.Warning : Severity.Error);
    }
}