namespace Onlyspans.Processes.Api.Contracts.Requests;

public sealed record ValidateProcessRequest
{
    public required string Yaml { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? EnvironmentId { get; init; }
}
