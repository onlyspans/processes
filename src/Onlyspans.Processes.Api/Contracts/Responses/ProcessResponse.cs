namespace Onlyspans.Processes.Api.Contracts.Responses;

public sealed record ProcessResponse
{
    public required Guid Id { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid EnvironmentId { get; init; }
    public required string ReleaseVersion { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public required int StepsCount { get; init; }
    public required IReadOnlyList<StepResponse> Steps { get; init; }
    public required IReadOnlyList<VariableResponse> Variables { get; init; }
}

public sealed record StepResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required int Order { get; init; }
    public string? Description { get; init; }
    public required string Type { get; init; }
    public string? Script { get; init; }
    public string? ScriptPath { get; init; }
    public bool Optional { get; init; }
    public bool Blocking { get; init; }
    public required string OnFailure { get; init; }
    public string? Timeout { get; init; }
    public required string Status { get; init; }
}

public sealed record VariableResponse
{
    public required string Name { get; init; }
    public required string Source { get; init; }
    public bool HasValue { get; init; }
}
