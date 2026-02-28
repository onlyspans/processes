using FluentValidation;
using Onlyspans.Processes.Api.Features.Parsing.Models;

namespace Onlyspans.Processes.Api.Features.Validation;

public sealed class ProcessDefinitionStructureValidator : AbstractValidator<ProcessDefinition>
{
    public ProcessDefinitionStructureValidator()
    {
        RuleFor(d => d.Steps)
            .NotNull()
            .WithMessage("Process definition must contain 'steps'");

        RuleFor(d => d.Steps)
            .Must(s => s is { Count: > 0 })
            .When(d => d.Steps is not null)
            .WithMessage("Process must have at least one step");

        When(d => d.Steps is { Count: > 0 }, () =>
        {
            RuleForEach(d => d.Steps)
                .ChildRules(entry =>
                {
                    entry.RuleFor(e => e.Key)
                        .NotEmpty()
                        .WithMessage("Step name (key) cannot be empty");

                    entry.RuleFor(e => e.Value)
                        .NotNull()
                        .WithMessage(e => $"Step '{e.Key}' has null definition");

                    entry.RuleFor(e => e.Value)
                        .SetValidator(new StepDefinitionValidator()!)
                        .When(e => e.Value is not null);
                });
        });

        When(d => d.Variables is { Count: > 0 }, () =>
        {
            RuleForEach(d => d.Variables)
                .ChildRules(v =>
                {
                    v.RuleFor(x => x.Name)
                        .NotEmpty()
                        .WithMessage("Variable name cannot be empty");

                    v.RuleFor(x => x)
                        .Must(x => !string.IsNullOrWhiteSpace(x.Value) || !string.IsNullOrWhiteSpace(x.Source))
                        .WithMessage(x => $"Variable '{x.Name}' must have either 'value' or 'source'");
                });

            RuleFor(d => d.Variables)
                .Must(vars =>
                {
                    var names = vars!.Select(v => v.Name).ToList();
                    return names.Distinct().Count() == names.Count;
                })
                .WithMessage("Duplicate variable names detected");
        });
    }
}
