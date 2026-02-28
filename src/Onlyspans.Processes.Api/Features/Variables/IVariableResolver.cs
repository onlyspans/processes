using Onlyspans.Processes.Api.Features.Parsing.Models;

namespace Onlyspans.Processes.Api.Features.Variables;

public interface IVariableResolver
{
    Task<VariableResolutionResult> ResolveAsync(
        IReadOnlyList<VariableDefinition> variables,
        VariableResolutionContext? context = null,
        CancellationToken ct = default);
}

public sealed record VariableResolutionContext
{
    public required Guid ProjectId { get; init; }
    public Guid? EnvironmentId { get; init; }
}

public sealed record VariableResolutionResult
{
    public required IReadOnlyDictionary<string, string> Resolved { get; init; }
    public required IReadOnlyList<string> Unresolved { get; init; }
}
