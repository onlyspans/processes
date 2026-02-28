namespace Onlyspans.Processes.Api.Domain.Enums;

public enum ProcessStatus
{
    Created,
    Validating,
    Validated,
    ValidationFailed,
    Running,
    AwaitingApproval,
    Completed,
    Failed,
    Cancelled,
    RollingBack,
    RolledBack,
}
