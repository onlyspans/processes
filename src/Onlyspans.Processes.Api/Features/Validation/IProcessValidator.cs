using Onlyspans.Processes.Api.Features.Parsing.Models;

namespace Onlyspans.Processes.Api.Features.Validation;

public interface IProcessValidator
{
    Task<ProcessValidationOutput> ValidateAsync(ProcessDefinition definition);
}

public sealed record ProcessValidationOutput
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>
    /// Variables referenced in steps but not defined inline — need external resolution.
    /// </summary>
    public required IReadOnlyList<string> UnresolvedVariables { get; init; }
}
