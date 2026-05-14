namespace Onlyspans.Processes.Api.Contracts.Requests;

public sealed record CreateProcessRequest
{
    public required Guid ProjectId { get; init; }
    public required Guid EnvironmentId { get; init; }
    public required string ReleaseVersion { get; init; }
    public required string Yaml { get; init; }
}
