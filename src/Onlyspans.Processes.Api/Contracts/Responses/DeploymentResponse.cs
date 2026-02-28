using Onlyspans.Processes.Api.Features.Deployment;

namespace Onlyspans.Processes.Api.Contracts.Responses;

public sealed record DeploymentResponse
{
    public required Guid DeploymentId { get; init; }
    public required Guid ProcessId { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? Summary { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorType { get; init; }
}

public sealed record DeploymentLogResponse
{
    public required Guid DeploymentId { get; init; }
    public required IReadOnlyList<DeploymentLogEntry> Entries { get; init; }
}
