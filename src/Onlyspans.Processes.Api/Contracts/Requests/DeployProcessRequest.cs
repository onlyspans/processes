namespace Onlyspans.Processes.Api.Contracts.Requests;

public sealed record DeployProcessRequest
{
    public required Guid ProcessId { get; init; }
    public required string TargetId { get; init; }
    public required string TargetType { get; init; }
    public required string SnapshotKey { get; init; }
}
