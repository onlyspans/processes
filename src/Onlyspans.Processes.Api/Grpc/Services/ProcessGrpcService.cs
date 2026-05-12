using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Onlyspans.Processes.Api.Features;
using Onlyspans.Processes.Api.Features.Deployment;
using Onlyspans.Processes.Api.Protos;

namespace Onlyspans.Processes.Api.Grpc.Services;

public sealed class ProcessGrpcService(
    ProcessService processService,
    DeploymentService deploymentService)
    : ProcessesGrpc.ProcessesGrpcBase
{
    public override async Task<GetProcessResult> GetProcess(
        GetProcessRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            return new GetProcessResult
            {
                NotFound = new NotFoundError { Message = $"Invalid process ID format: '{request.Id}'" },
            };

        try
        {
            var result = await processService.GetByIdAsync(id, context.CancellationToken);

            if (result is null)
                return new GetProcessResult
                {
                    NotFound = new NotFoundError { Message = $"Process '{request.Id}' not found" },
                };

            return new GetProcessResult { Success = MapToData(result) };
        }
        catch (Exception ex)
        {
            return new GetProcessResult
            {
                InternalError = new InternalError { Message = ex.Message, Trace = ex.StackTrace },
            };
        }
    }

    public override async Task<ListByProjectResult> ListByProject(
        ListByProjectRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.ProjectId, out var projectId))
            return new ListByProjectResult
            {
                InternalError = new InternalError { Message = $"Invalid project ID format: '{request.ProjectId}'" },
            };

        try
        {
            var results = await processService.ListByProjectAsync(projectId, context.CancellationToken);

            var reply = new ListByProjectResult.Types.Success();
            reply.Processes.AddRange(results.Select(MapToData));
            return new ListByProjectResult { Success = reply };
        }
        catch (Exception ex)
        {
            return new ListByProjectResult
            {
                InternalError = new InternalError { Message = ex.Message, Trace = ex.StackTrace },
            };
        }
    }

    public override async Task<CreateProcessResult> CreateProcess(
        Protos.CreateProcessRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.ProjectId, out var projectId))
            return new CreateProcessResult
            {
                ValidationError = new CreateProcessResult.Types.ValidationError
                {
                    Errors = { $"Invalid project ID format: '{request.ProjectId}'" },
                },
            };

        if (!Guid.TryParse(request.EnvironmentId, out var environmentId))
            return new CreateProcessResult
            {
                ValidationError = new CreateProcessResult.Types.ValidationError
                {
                    Errors = { $"Invalid environment ID format: '{request.EnvironmentId}'" },
                },
            };

        try
        {
            var result = await processService.CreateAsync(
                projectId,
                environmentId,
                request.ReleaseVersion,
                request.Yaml,
                context.CancellationToken);

            return new CreateProcessResult { Success = MapToData(result) };
        }
        catch (InvalidOperationException ex)
        {
            return new CreateProcessResult
            {
                ValidationError = new CreateProcessResult.Types.ValidationError
                {
                    Errors = { ex.Message },
                },
            };
        }
        catch (Exception ex)
        {
            return new CreateProcessResult
            {
                InternalError = new InternalError { Message = ex.Message, Trace = ex.StackTrace },
            };
        }
    }

    public override async Task<ValidateProcessResult> ValidateProcess(
        Protos.ValidateProcessRequest request,
        ServerCallContext context)
    {
        Guid? projectId = string.IsNullOrWhiteSpace(request.ProjectId)
            ? null
            : Guid.TryParse(request.ProjectId, out var pid) ? pid : null;

        Guid? environmentId = string.IsNullOrWhiteSpace(request.EnvironmentId)
            ? null
            : Guid.TryParse(request.EnvironmentId, out var eid) ? eid : null;

        try
        {
            var result = await processService.ValidateAsync(
                request.Yaml, projectId, environmentId, context.CancellationToken);

            if (!result.IsValid)
            {
                var error = new ValidateProcessResult.Types.ValidationError();
                error.Errors.AddRange(result.Errors);
                error.Warnings.AddRange(result.Warnings);
                return new ValidateProcessResult { ValidationError = error };
            }

            var success = new ValidateProcessResult.Types.Success();
            success.Warnings.AddRange(result.Warnings);
            success.UnresolvedVariables.AddRange(result.UnresolvedVariables);

            if (result.Steps is not null)
            {
                success.Steps.AddRange(result.Steps.Select(s => new ResolvedStep
                {
                    Name        = s.Name,
                    Order       = s.Order,
                    Description = s.Description ?? string.Empty,
                    Type        = s.Type,
                    Script      = s.Script ?? string.Empty,
                    ScriptPath  = s.ScriptPath ?? string.Empty,
                    Optional    = s.Optional,
                    Blocking    = s.Blocking,
                    OnFailure   = s.OnFailure ?? string.Empty,
                    Timeout     = s.Timeout ?? string.Empty,
                }));
            }

            return new ValidateProcessResult { Success = success };
        }
        catch (Exception ex)
        {
            return new ValidateProcessResult
            {
                InternalError = new InternalError { Message = ex.Message, Trace = ex.StackTrace },
            };
        }
    }

    public override async Task<DeployProcessResult> DeployProcess(
        Protos.DeployProcessRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.ProcessId, out var processId))
            return new DeployProcessResult
            {
                Error = new DeployProcessResult.Types.Error
                {
                    Code = "INVALID_ARGUMENT", Message = "Invalid process ID format",
                },
            };

        if (string.IsNullOrWhiteSpace(request.EnvironmentId))
            return new DeployProcessResult
            {
                Error = new DeployProcessResult.Types.Error
                {
                    Code = "INVALID_ARGUMENT", Message = "environment_id is required",
                },
            };

        if (string.IsNullOrWhiteSpace(request.SnapshotKey))
            return new DeployProcessResult
            {
                Error = new DeployProcessResult.Types.Error
                {
                    Code = "INVALID_ARGUMENT", Message = "snapshot_key is required",
                },
            };

        try
        {
            var result = await deploymentService.ExecuteAsync(
                processId,
                request.TargetId,
                request.TargetType,
                request.SnapshotKey,
                context.CancellationToken);

            var success = new DeployProcessResult.Types.Success
            {
                DeploymentId = result.DeploymentId.ToString(),
                ProcessId    = result.ProcessId.ToString(),
                Status       = result.Status,
            };

            // Protobuf string fields reject null; Summary is unset when e.g. deployment pauses for approval.
            if (result.Summary is not null)
                success.Summary = result.Summary;

            var reply = new DeployProcessResult { Success = success };

            if (result.CompletedAt.HasValue)
                reply.Success.CompletedAt = Timestamp.FromDateTimeOffset(result.CompletedAt.Value);

            return reply;
        }
        catch (InvalidOperationException ex)
        {
            return new DeployProcessResult
            {
                Error = new DeployProcessResult.Types.Error
                {
                    Code = "PROCESS_ERROR", Message = ex.Message,
                },
            };
        }
        catch (Exception ex)
        {
            return new DeployProcessResult
            {
                InternalError = new InternalError { Message = ex.Message, Trace = ex.StackTrace },
            };
        }
    }

    private static ProcessData MapToData(Contracts.Responses.ProcessResponse process)
    {
        var data = new ProcessData
        {
            Id             = process.Id.ToString(),
            ProjectId      = process.ProjectId.ToString(),
            EnvironmentId  = process.EnvironmentId.ToString(),
            ReleaseVersion = process.ReleaseVersion,
            Status         = process.Status,
            CreatedAt      = Timestamp.FromDateTimeOffset(process.CreatedAt),
            StepsCount     = process.StepsCount,
        };

        if (process.UpdatedAt.HasValue)
            data.UpdatedAt = Timestamp.FromDateTimeOffset(process.UpdatedAt.Value);

        if (process.CompletedAt.HasValue)
            data.CompletedAt = Timestamp.FromDateTimeOffset(process.CompletedAt.Value);

        data.Steps.AddRange(process.Steps.Select(s => new StepData
        {
            Id          = s.Id.ToString(),
            Name        = s.Name,
            Order       = s.Order,
            Description = s.Description ?? string.Empty,
            Type        = s.Type,
            Script      = s.Script ?? string.Empty,
            ScriptPath  = s.ScriptPath ?? string.Empty,
            Optional    = s.Optional,
            Blocking    = s.Blocking,
            OnFailure   = s.OnFailure,
            Timeout     = s.Timeout ?? string.Empty,
            Status      = s.Status,
        }));

        data.Variables.AddRange(process.Variables.Select(v => new VariableData
        {
            Name     = v.Name,
            Source   = v.Source,
            HasValue = v.HasValue,
        }));

        return data;
    }
}
