using System.Diagnostics.CodeAnalysis;
using OMniX.Core.Services.CommandService.Models;

namespace OMniX.Core.Services.CommandService;

public static class SpecialVariables
{
    public static IReadOnlyList<string> AllVariables => [TargetPath];


    public const string TargetPath = "{{TargetPath}}";


    [return: NotNullIfNotNull(nameof(input))]
    public static string? ReplaceVariables(string? input, SpecialVariablesInput? specialVariables)
    {
        if (input is null)
            return null;

        if (specialVariables is null || !specialVariables.HasAnySpecialVariables())
            return input;

        foreach (var variable in specialVariables.GetSpecialVariables())
        {
            input = input.Replace(variable, specialVariables.GetVariable(variable));
        }

        return input;
    }
}