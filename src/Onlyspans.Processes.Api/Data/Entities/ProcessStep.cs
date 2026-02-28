using Onlyspans.Processes.Api.Domain.Enums;

namespace Onlyspans.Processes.Api.Data.Entities;

public class ProcessStep
{
    public Guid Id { get; set; }

    public Guid ProcessId { get; set; }

    public string Name { get; set; } = null!;

    public int Order { get; set; }

    public string? Description { get; set; }

    public StepType Type { get; set; }

    public string? Script { get; set; }

    public string? ScriptPath { get; set; }

    public bool Optional { get; set; }

    public bool Blocking { get; set; } = true;

    public OnFailureAction OnFailure { get; set; }

    public string? Timeout { get; set; }

    public StepStatus Status { get; set; }

    public DeploymentProcess Process { get; set; } = null!;
}
