using FluentValidation;
using OMniX.Core.Contracts.Services;
using OMniX.WinUI.ViewModels.SubVms;

namespace OMniX.WinUI.Validators;

public class ContainsAnyFileSystemEntryWithNames : AbstractValidator<PathPicker>
{
    private static readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

    public ContainsAnyFileSystemEntryWithNames(IEnumerable<string> filenames, string? customMessage = null, bool warning = false)
    {
        var fileNamesArray = filenames.ToArray();
        var filenamesLowerArray = fileNamesArray.Select(name => name.ToLower()).ToArray();

        customMessage ??=
            string.Format(_localizer.GetLocalizedStringOrDefault("/CharacterManager/Valid_FilesMissing", "文件夹里找不到以下名称的文件或文件夹：{0}"), string.Join(" 或 ", fileNamesArray));

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