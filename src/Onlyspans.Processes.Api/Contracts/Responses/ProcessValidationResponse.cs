namespace Onlyspans.Processes.Api.Contracts.Responses;

public sealed record ProcessValidationResponse
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> UnresolvedVariables { get; init; }
    public IReadOnlyList<ResolvedStepResponse>? Steps { get; init; }
}

public sealed record ResolvedStepResponse
{
    public required string Name { get; init; }
    public required int Order { get; init; }
    public string? Description { get; init; }
    public required string Type { get; init; }
    public string? Script { get; init; }
    public string? ScriptPath { get; init; }
    public bool Optional { get; init; }
    public bool Blocking { get; init; }
    public string? OnFailure { get; init; }
    public string? Timeout { get; init; }
}
