using System.Text.RegularExpressions;
using FluentValidation;
using Onlyspans.Processes.Api.Features.Parsing.Models;

namespace Onlyspans.Processes.Api.Features.Validation;

public sealed partial class CompositeProcessValidator : IProcessValidator
{
    private readonly IValidator<ProcessDefinition> _structureValidator;
    private readonly PipelineGraphValidator _graphValidator;

    public CompositeProcessValidator(
        IValidator<ProcessDefinition> structureValidator,
        PipelineGraphValidator graphValidator)
    {
        _structureValidator = structureValidator;
        _graphValidator     = graphValidator;
    }

    public async Task<ProcessValidationOutput> ValidateAsync(ProcessDefinition definition)
    {
        var errors   = new List<string>();
        var warnings = new List<string>();

        var structureResult = _structureValidator.Validate(definition);
        if (!structureResult.IsValid)
        {
            errors.AddRange(structureResult.Errors.Select(e => e.ErrorMessage));
            return new ProcessValidationOutput
            {
                IsValid             = false,
                Errors              = errors,
                Warnings            = warnings,
                UnresolvedVariables = [],
            };
        }

        var graphResult = await _graphValidator.ValidateAsync(definition);
        errors.AddRange(graphResult.Errors);

        var (variableErrors, unresolvedVars) = ValidateVariableReferences(definition);
        errors.AddRange(variableErrors);

        if (unresolvedVars.Count > 0)
        {
            warnings.Add(
                $"Variables requiring external resolution: {string.Join(", ", unresolvedVars)}");
        }

        return new ProcessValidationOutput
        {
            IsValid             = errors.Count == 0,
            Errors              = errors,
            Warnings            = warnings,
            UnresolvedVariables = unresolvedVars,
        };
    }

    private static (List<string> Errors, List<string> Unresolved) ValidateVariableReferences(
        ProcessDefinition definition)
    {
        var errors     = new List<string>();
        var unresolved = new List<string>();

        var inlineVars = (definition.Variables ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v.Value))
            .Select(v => v.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var secretVars = (definition.Variables ?? [])
            .Where(v => string.Equals(v.Source, "secrets", StringComparison.OrdinalIgnoreCase))
            .Select(v => v.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allDeclared = (definition.Variables ?? [])
            .Select(v => v.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (stepName, stepDef, _) in definition.GetOrderedSteps())
        {
            var script = stepDef.Script;
            if (string.IsNullOrWhiteSpace(script)) continue;

            var references = VariableReferenceRegex().Matches(script);
            foreach (Match match in references)
            {
                var varName = match.Groups[1].Value;
                if (allDeclared.Contains(varName)) continue;

                if (secretVars.Contains(varName))
                {
                    unresolved.Add(varName);
                    continue;
                }

                unresolved.Add(varName);
            }
        }

        foreach (var v in definition.Variables ?? [])
        {
            if (string.Equals(v.Source, "secrets", StringComparison.OrdinalIgnoreCase)
                && !unresolved.Contains(v.Name))
            {
                unresolved.Add(v.Name);
            }
        }

        return (errors, unresolved.Distinct().ToList());
    }

    [GeneratedRegex(@"\$\{?([A-Z_][A-Z0-9_]*)\}?", RegexOptions.Compiled)]
    private static partial Regex VariableReferenceRegex();
}
