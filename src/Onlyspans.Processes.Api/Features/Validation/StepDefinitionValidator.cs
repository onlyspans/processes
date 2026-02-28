using FluentValidation;
using Onlyspans.Processes.Api.Features.Parsing.Models;

namespace Onlyspans.Processes.Api.Features.Validation;

public sealed class StepDefinitionValidator : AbstractValidator<StepDefinition>
{
    private static readonly HashSet<string> KnownTypes = ["script", "approval"];
    private static readonly HashSet<string> KnownOnFailure = ["abort", "continue", "rollback"];

    public StepDefinitionValidator()
    {
        RuleFor(s => s)
            .Must(s => !string.IsNullOrWhiteSpace(s.Script)
                     || !string.IsNullOrWhiteSpace(s.ScriptPath)
                     || string.Equals(s.Type, "approval", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Step must have either 'script', 'script-path', or type 'approval'");

        RuleFor(s => s)
            .Must(s => string.IsNullOrWhiteSpace(s.Script) || string.IsNullOrWhiteSpace(s.ScriptPath))
            .WithMessage("Step cannot have both 'script' and 'script-path'");

        When(s => !string.IsNullOrWhiteSpace(s.Type), () =>
        {
            RuleFor(s => s.Type)
                .Must(t => KnownTypes.Contains(t!.ToLowerInvariant()))
                .WithMessage(s => $"Unknown step type '{s.Type}'. Known types: {string.Join(", ", KnownTypes)}");
        });

        When(s => !string.IsNullOrWhiteSpace(s.OnFailure), () =>
        {
            RuleFor(s => s.OnFailure)
                .Must(a => KnownOnFailure.Contains(a!.ToLowerInvariant()))
                .WithMessage(s => $"Unknown on_failure action '{s.OnFailure}'. Known actions: {string.Join(", ", KnownOnFailure)}");
        });

        When(s => string.Equals(s.Type, "approval", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(s => s.Approvers)
                .NotNull()
                .WithMessage("Approval step must specify at least one approver");

            RuleFor(s => s.Approvers)
                .Must(a => a is { Count: > 0 })
                .When(s => s.Approvers is not null)
                .WithMessage("Approval step must have at least one approver entry");
        });
    }
}
