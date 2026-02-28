namespace Onlyspans.Processes.Api.Features.Deployment;

public interface IDeploymentLogWriter
{
    Task AppendAsync(Guid deploymentId, DeploymentLogEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<DeploymentLogEntry>> ReadAsync(Guid deploymentId, CancellationToken ct = default);
    Task<IReadOnlyList<DeploymentLogEntry>> ReadFromOffsetAsync(
        Guid deploymentId, long offsetBytes, CancellationToken ct = default);
}

public sealed record DeploymentLogEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Level { get; init; }
    public required string Message { get; init; }
    public string? Source { get; init; }
}
