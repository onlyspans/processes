using Onlyspans.Processes.Api.Domain.Enums;

namespace Onlyspans.Processes.Api.Data.Entities;

public class ProcessVariable
{
    public Guid Id { get; set; }

    public Guid ProcessId { get; set; }

    public string Name { get; set; } = null!;

    public string? Value { get; set; }

    public VariableSource Source { get; set; }

    public DeploymentProcess Process { get; set; } = null!;
}
