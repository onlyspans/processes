using Onlyspans.Processes.Api.Domain.Enums;

namespace Onlyspans.Processes.Api.Data.Entities;

public class DeploymentProcess
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid EnvironmentId { get; set; }

    public string ReleaseVersion { get; set; } = null!;

    public ProcessStatus Status { get; set; }

    public string? RawYaml { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public ICollection<ProcessStep> Steps { get; set; } = [];

    public ICollection<ProcessVariable> Variables { get; set; } = [];
}
