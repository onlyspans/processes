using Microsoft.EntityFrameworkCore;
using Onlyspans.Processes.Api.Contracts.Responses;
using Onlyspans.Processes.Api.Data.Contexts;
using Onlyspans.Processes.Api.Data.Entities;
using Onlyspans.Processes.Api.Domain.Enums;
using Onlyspans.Processes.Api.Features.Parsing;
using Onlyspans.Processes.Api.Features.Parsing.Models;
using Onlyspans.Processes.Api.Features.Validation;
using Onlyspans.Processes.Api.Features.Variables;

namespace Onlyspans.Processes.Api.Features;

public sealed class ProcessService
{
    private readonly IProcessDefinitionParser _parser;
    private readonly IProcessValidator _validator;
    private readonly IVariableResolver _variableResolver;
    private readonly ProcessesDbContext _db;

    public ProcessService(
        IProcessDefinitionParser parser,
        IProcessValidator validator,
        IVariableResolver variableResolver,
        ProcessesDbContext db)
    {
        _parser           = parser;
        _validator        = validator;
        _variableResolver = variableResolver;
        _db               = db;
    }

    public async Task<ProcessValidationResponse> ValidateAsync(
        string yaml,
        Guid? projectId = null,
        Guid? environmentId = null,
        CancellationToken ct = default)
    {
        ProcessDefinition definition;
        try
        {
            definition = _parser.Parse(yaml);
        }
        catch (Exception ex)
        {
            return new ProcessValidationResponse
            {
                IsValid             = false,
                Errors              = [$"YAML parse error: {ex.Message}"],
                Warnings            = [],
                UnresolvedVariables = [],
            };
        }

        var validation = await _validator.ValidateAsync(definition);
        if (!validation.IsValid)
        {
            return new ProcessValidationResponse
            {
                IsValid             = false,
                Errors              = validation.Errors,
                Warnings            = validation.Warnings,
                UnresolvedVariables = validation.UnresolvedVariables,
            };
        }

        var context = projectId.HasValue
            ? new VariableResolutionContext
            {
                ProjectId     = projectId.Value,
                EnvironmentId = environmentId,
            }
            : null;

        var resolution = await _variableResolver.ResolveAsync(
            definition.Variables ?? [], context, ct);

        var resolvedSteps = BuildResolvedSteps(definition, resolution.Resolved);

        return new ProcessValidationResponse
        {
            IsValid             = true,
            Errors              = [],
            Warnings            = validation.Warnings,
            UnresolvedVariables = validation.UnresolvedVariables,
            Steps               = resolvedSteps,
        };
    }

    public async Task<ProcessResponse> CreateAsync(
        Guid projectId,
        Guid environmentId,
        string releaseVersion,
        string yaml,
        CancellationToken ct = default)
    {
        var definition = _parser.Parse(yaml);
        var validation = await _validator.ValidateAsync(definition);

        if (!validation.IsValid)
            throw new InvalidOperationException(
                $"Process definition is invalid: {string.Join("; ", validation.Errors)}");

        var resolution = await _variableResolver.ResolveAsync(
            definition.Variables ?? [],
            new VariableResolutionContext
            {
                ProjectId     = projectId,
                EnvironmentId = environmentId,
            },
            ct);

        var process = new DeploymentProcess
        {
            Id             = Guid.NewGuid(),
            ProjectId      = projectId,
            EnvironmentId  = environmentId,
            ReleaseVersion = releaseVersion,
            Status         = ProcessStatus.Validated,
            RawYaml        = yaml,
            CreatedAt      = DateTimeOffset.UtcNow,
        };

        foreach (var (name, stepDef, order) in definition.GetOrderedSteps())
        {
            var script = stepDef.Script;
            if (!string.IsNullOrWhiteSpace(script))
                script = ScriptVariableSubstitutor.Substitute(script, resolution.Resolved);

            process.Steps.Add(new ProcessStep
            {
                Id          = Guid.NewGuid(),
                ProcessId   = process.Id,
                Name        = name,
                Order       = order,
                Description = stepDef.Description,
                Type        = ParseStepType(stepDef.Type),
                Script      = script,
                ScriptPath  = stepDef.ScriptPath,
                Optional    = stepDef.Optional,
                Blocking    = stepDef.Blocking,
                OnFailure   = ParseOnFailure(stepDef.OnFailure),
                Timeout     = stepDef.Timeout,
                Status      = StepStatus.Pending,
            });
        }

        foreach (var v in definition.Variables ?? [])
        {
            process.Variables.Add(new ProcessVariable
            {
                Id        = Guid.NewGuid(),
                ProcessId = process.Id,
                Name      = v.Name,
                Value     = v.Value,
                Source    = ParseVariableSource(v.Source),
            });
        }

        _db.Processes.Add(process);
        await _db.SaveChangesAsync(ct);

        return MapToResponse(process);
    }

    public async Task<ProcessResponse?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var process = await _db.Processes
            .Include(p => p.Steps.OrderBy(s => s.Order))
            .Include(p => p.Variables)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        return process is null ? null : MapToResponse(process);
    }

    public async Task<IReadOnlyList<ProcessResponse>> ListByProjectAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var processes = await _db.Processes
            .Include(p => p.Steps.OrderBy(s => s.Order))
            .Include(p => p.Variables)
            .Where(p => p.ProjectId == projectId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

        return processes.Select(MapToResponse).ToList();
    }

    public async Task<ProcessResponse?> GetByProjectAndVersionAsync(
        Guid projectId,
        string releaseVersion,
        CancellationToken ct = default)
    {
        var process = await _db.Processes
            .Include(p => p.Steps.OrderBy(s => s.Order))
            .Include(p => p.Variables)
            .FirstOrDefaultAsync(
                p => p.ProjectId == projectId && p.ReleaseVersion == releaseVersion, ct);

        return process is null ? null : MapToResponse(process);
    }

    private static List<ResolvedStepResponse> BuildResolvedSteps(
        ProcessDefinition definition,
        IReadOnlyDictionary<string, string> resolvedVars)
    {
        return definition.GetOrderedSteps().Select(s =>
        {
            var script = s.Definition.Script;
            if (!string.IsNullOrWhiteSpace(script))
                script = ScriptVariableSubstitutor.Substitute(script, resolvedVars);

            return new ResolvedStepResponse
            {
                Name        = s.Name,
                Order       = s.Order,
                Description = s.Definition.Description,
                Type        = s.Definition.Type ?? "script",
                Script      = script,
                ScriptPath  = s.Definition.ScriptPath,
                Optional    = s.Definition.Optional,
                Blocking    = s.Definition.Blocking,
                OnFailure   = s.Definition.OnFailure,
                Timeout     = s.Definition.Timeout,
            };
        }).ToList();
    }

    private static ProcessResponse MapToResponse(DeploymentProcess process)
    {
        return new ProcessResponse
        {
            Id             = process.Id,
            ProjectId      = process.ProjectId,
            EnvironmentId  = process.EnvironmentId,
            ReleaseVersion = process.ReleaseVersion,
            Status         = process.Status.ToString(),
            CreatedAt      = process.CreatedAt,
            UpdatedAt      = process.UpdatedAt,
            CompletedAt    = process.CompletedAt,
            StepsCount     = process.Steps.Count,
            Steps = process.Steps.OrderBy(s => s.Order).Select(s => new StepResponse
            {
                Id          = s.Id,
                Name        = s.Name,
                Order       = s.Order,
                Description = s.Description,
                Type        = s.Type.ToString(),
                Script      = s.Script,
                ScriptPath  = s.ScriptPath,
                Optional    = s.Optional,
                Blocking    = s.Blocking,
                OnFailure   = s.OnFailure.ToString(),
                Timeout     = s.Timeout,
                Status      = s.Status.ToString(),
            }).ToList(),
            Variables = process.Variables.Select(v => new VariableResponse
            {
                Name     = v.Name,
                Source   = v.Source.ToString(),
                HasValue = !string.IsNullOrWhiteSpace(v.Value),
            }).ToList(),
        };
    }

    private static StepType ParseStepType(string? type) => type?.ToLowerInvariant() switch
    {
        "approval" => StepType.Approval,
        _          => StepType.Script,
    };

    private static OnFailureAction ParseOnFailure(string? action) => action?.ToLowerInvariant() switch
    {
        "continue" => OnFailureAction.Continue,
        "rollback" => OnFailureAction.Rollback,
        _          => OnFailureAction.Abort,
    };

    private static VariableSource ParseVariableSource(string? source) => source?.ToLowerInvariant() switch
    {
        "secrets"  => VariableSource.Secrets,
        "external" => VariableSource.External,
        _          => VariableSource.Inline,
    };
}
