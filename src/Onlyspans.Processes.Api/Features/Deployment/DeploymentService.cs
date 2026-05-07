using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Onlyspans.Processes.Api.Contracts.Responses;
using Onlyspans.Processes.Api.Data.Contexts;
using Onlyspans.Processes.Api.Data.Entities;
using Onlyspans.Processes.Api.Domain.Enums;
using Onlyspans.Processes.Api.Features.Parsing.Models;
using Onlyspans.Processes.Api.Features.Pipeline;
using Onlyspans.Processes.Api.Features.Variables;
using Onlyspans.Processes.Api.Grpc.Services;
using Worker.Communication;

namespace Onlyspans.Processes.Api.Features.Deployment;

/// <summary>
/// Orchestrates deployment execution following ADR-4 from target-arch.md:
/// Processes resolves secrets + snapshot via ArtifactStorage, sends ready package to Worker,
/// Worker streams logs back, Processes writes them to append-only files.
/// </summary>
public sealed class DeploymentService(
    ProcessesDbContext db,
    WorkerGrpcService workerService,
    ArtifactStorageGrpcService artifactStorageService,
    IVariableResolver variableResolver,
    IDeploymentLogWriter logWriter,
    ILogger<DeploymentService> logger)
{
    public async Task<DeploymentResponse> ExecuteAsync(
        Guid processId,
        string targetId,
        string targetType,
        string snapshotKey,
        CancellationToken ct = default)
    {
        var process = await db.Processes
            .Include(p => p.Steps.OrderBy(s => s.Order))
            .Include(p => p.Variables)
            .FirstOrDefaultAsync(p => p.Id == processId, ct)
            ?? throw new InvalidOperationException($"Process '{processId}' not found");

        var deploymentId = Guid.NewGuid();
        process.Status = ProcessStatus.Running;
        process.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var resolvedVariables = await ResolveAllVariablesAsync(process, ct);
        var resolvedSnapshotKey = await ResolveSnapshotAsync(deploymentId, snapshotKey, ct);

        var orderedSteps = process.Steps
            .OrderBy(s => s.Order)
            .ToList();

        var fsmSteps = orderedSteps
            .Select(s => (s.Name, new StepDefinition
            {
                Script     = s.Script,
                ScriptPath = s.ScriptPath,
                OnFailure  = s.OnFailure.ToString().ToLowerInvariant(),
                Optional   = s.Optional,
                Type       = s.Type.ToString().ToLowerInvariant(),
            }, s.Order))
            .ToList();

        var fsm = new PipelineStateMachine(fsmSteps);
        fsm.Start();

        string? errorMessage = null;
        string? errorType = null;
        string? summary = null;
        DateTimeOffset? completedAt = null;

        while (!fsm.IsTerminal)
        {
            var currentStepInfo = fsm.GetCurrentStep();
            if (currentStepInfo is null)
                break;

            var currentDbStep = orderedSteps.First(s => s.Name == currentStepInfo.Value.Name);

            if (currentDbStep.Type == StepType.Approval)
            {
                process.Status = ProcessStatus.AwaitingApproval;
                currentDbStep.Status = StepStatus.Running;
                await db.SaveChangesAsync(ct);

                return new DeploymentResponse
                {
                    DeploymentId = deploymentId,
                    ProcessId    = processId,
                    Status       = ProcessStatus.AwaitingApproval.ToString(),
                };
            }

            currentDbStep.Status = StepStatus.Running;
            await db.SaveChangesAsync(ct);

            var package = BuildDeploymentPackage(
                deploymentId, process, currentDbStep,
                targetId, targetType, resolvedSnapshotKey, resolvedVariables);

            var stepResult = await ExecuteStepAsync(deploymentId, package, ct);

            if (stepResult.Success)
            {
                currentDbStep.Status = StepStatus.Succeeded;
                await db.SaveChangesAsync(ct);
                fsm.StepSucceeded();

                summary = stepResult.Summary;
                completedAt = stepResult.CompletedAt;
            }
            else
            {
                currentDbStep.Status = StepStatus.Failed;
                await db.SaveChangesAsync(ct);

                errorMessage = stepResult.ErrorMessage;
                errorType = stepResult.ErrorTypeName;

                try
                {
                    fsm.StepFailed();
                }
                catch (InvalidOperationException)
                {
                    break;
                }
            }
        }

        if (fsm.CurrentState == PipelineStateMachine.StateRollingBack)
        {
            process.Status = ProcessStatus.RollingBack;
            await db.SaveChangesAsync(ct);

            var rollbackSucceeded = await ExecuteRollbackAsync(
                deploymentId, orderedSteps, targetId, targetType,
                resolvedSnapshotKey, resolvedVariables, process, ct);

            if (rollbackSucceeded)
            {
                fsm.RollbackCompleted();
                process.Status = ProcessStatus.RolledBack;
            }
            else
            {
                fsm.RollbackFailed();
                process.Status = ProcessStatus.Failed;
            }

            process.CompletedAt = DateTimeOffset.UtcNow;
        }
        else if (fsm.CurrentState == PipelineStateMachine.StateCompleted)
        {
            process.Status = ProcessStatus.Completed;
            process.CompletedAt = completedAt ?? DateTimeOffset.UtcNow;
        }
        else if (fsm.CurrentState == PipelineStateMachine.StateFailed)
        {
            process.Status = ProcessStatus.Failed;
            process.CompletedAt = DateTimeOffset.UtcNow;
        }

        process.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return new DeploymentResponse
        {
            DeploymentId = deploymentId,
            ProcessId    = processId,
            Status       = process.Status.ToString(),
            CompletedAt  = process.CompletedAt,
            Summary      = summary,
            ErrorMessage = errorMessage,
            ErrorType    = errorType,
        };
    }

    public async Task<DeploymentLogResponse> GetLogsAsync(
        Guid deploymentId,
        CancellationToken ct = default)
    {
        var entries = await logWriter.ReadAsync(deploymentId, ct);
        return new DeploymentLogResponse
        {
            DeploymentId = deploymentId,
            Entries      = entries,
        };
    }

    private async Task<bool> ExecuteRollbackAsync(
        Guid deploymentId,
        List<ProcessStep> orderedSteps,
        string targetId,
        string targetType,
        string snapshotKey,
        Dictionary<string, string> resolvedVariables,
        DeploymentProcess process,
        CancellationToken ct)
    {
        var completedSteps = orderedSteps
            .Where(s => s.Status == StepStatus.Succeeded)
            .OrderByDescending(s => s.Order)
            .ToList();

        foreach (var step in completedSteps)
        {
            if (string.IsNullOrWhiteSpace(step.Script) &&
                string.IsNullOrWhiteSpace(step.ScriptPath))
                continue;

            logger.LogInformation(
                "Rolling back step '{StepName}' for deployment {DeploymentId}",
                step.Name, deploymentId);

            var package = BuildDeploymentPackage(
                deploymentId, process, step,
                targetId, targetType, snapshotKey, resolvedVariables);

            var result = await ExecuteStepAsync(deploymentId, package, ct);

            if (!result.Success)
            {
                logger.LogError(
                    "Rollback of step '{StepName}' failed: {Error}",
                    step.Name, result.ErrorMessage);
                return false;
            }
        }

        return true;
    }

    private async Task<StepExecutionResult> ExecuteStepAsync(
        Guid deploymentId,
        DeploymentPackage package,
        CancellationToken ct)
    {
        try
        {
            using var call = workerService.ExecuteDeployment(package, ct);

            await foreach (var msg in call.ResponseStream.ReadAllAsync(ct))
            {
                switch (msg.MessageCase)
                {
                    case DeploymentMessage.MessageOneofCase.Log:
                        var logChunk = msg.Log;
                        await logWriter.AppendAsync(deploymentId, new DeploymentLogEntry
                        {
                            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(logChunk.Timestamp),
                            Level     = logChunk.Level.ToString(),
                            Message   = logChunk.Message,
                            Source    = logChunk.HasSource ? logChunk.Source : null,
                        }, ct);
                        break;

                    case DeploymentMessage.MessageOneofCase.Result:
                        var result = msg.Result;
                        return result.ResultCase switch
                        {
                            DeploymentResult.ResultOneofCase.Success => new StepExecutionResult
                            {
                                Success     = true,
                                Summary     = result.Success.Summary,
                                CompletedAt = DateTimeOffset.FromUnixTimeMilliseconds(
                                    result.Success.CompletedAt),
                            },

                            DeploymentResult.ResultOneofCase.Error => new StepExecutionResult
                            {
                                Success       = false,
                                ErrorMessage  = result.Error.Message,
                                ErrorTypeName = result.Error.ErrorType.ToString(),
                            },

                            _ => new StepExecutionResult
                            {
                                Success      = false,
                                ErrorMessage = "Unknown result type from Worker",
                            },
                        };
                }
            }

            return new StepExecutionResult
            {
                Success      = false,
                ErrorMessage = "Worker stream ended without a result message",
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute step via Worker for deployment {DeploymentId}", deploymentId);

            await logWriter.AppendAsync(deploymentId, new DeploymentLogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level     = "ERROR",
                Message   = $"Worker communication error: {ex.Message}",
                Source    = "processes",
            }, ct);

            return new StepExecutionResult
            {
                Success       = false,
                ErrorMessage  = ex.Message,
                ErrorTypeName = "ERROR_TYPE_INTERNAL",
            };
        }
    }

    /// <summary>
    /// Validates that the snapshot exists in ArtifactStorage.
    /// Returns the verified snapshot key to pass to Worker.
    /// </summary>
    private async Task<string> ResolveSnapshotAsync(
        Guid deploymentId,
        string snapshotKey,
        CancellationToken ct)
    {
        logger.LogInformation(
            "Resolving snapshot {SnapshotKey} from ArtifactStorage for deployment {DeploymentId}",
            snapshotKey, deploymentId);

        var parts = snapshotKey.Split('@', 2);
        var key = parts[0];
        var version = parts.Length > 1 ? parts[1] : snapshotKey;

        var result = await artifactStorageService.GetSnapshotInfoAsync(key, version, ct);

        switch (result)
        {
            case SnapshotResult.Ok ok:
                logger.LogInformation(
                    "Snapshot {Key}@{Version} resolved: {SizeBytes} bytes, type={ContentType}",
                    ok.Key, ok.Version, ok.SizeBytes, ok.ContentType);
                return snapshotKey;

            case SnapshotResult.NotFound notFound:
                throw new InvalidOperationException(
                    $"Snapshot '{notFound.Key}@{notFound.Version}' not found in ArtifactStorage: {notFound.Message}");

            case SnapshotResult.Error error:
                throw new InvalidOperationException(
                    $"Failed to retrieve snapshot from ArtifactStorage: {error.Message}");

            default:
                throw new InvalidOperationException("Unexpected response from ArtifactStorage");
        }
    }

    private async Task<Dictionary<string, string>> ResolveAllVariablesAsync(
        DeploymentProcess process,
        CancellationToken ct)
    {
        var variableDefinitions = process.Variables
            .Select(v => new VariableDefinition
            {
                Name   = v.Name,
                Value  = v.Value,
                Source = v.Source.ToString().ToLowerInvariant(),
            })
            .ToList();

        var resolution = await variableResolver.ResolveAsync(
            variableDefinitions,
            new VariableResolutionContext
            {
                ProjectId     = process.ProjectId,
                EnvironmentId = process.EnvironmentId,
            },
            ct);

        return new Dictionary<string, string>(
            resolution.Resolved, StringComparer.OrdinalIgnoreCase);
    }

    private static DeploymentPackage BuildDeploymentPackage(
        Guid deploymentId,
        DeploymentProcess process,
        ProcessStep step,
        string targetId,
        string targetType,
        string snapshotKey,
        Dictionary<string, string> resolvedVariables)
    {
        var package = new DeploymentPackage
        {
            DeploymentId  = deploymentId.ToString(),
            ProjectId     = process.ProjectId.ToString(),
            EnvironmentId = process.EnvironmentId.ToString(),
            TargetId      = targetId,
            SnapshotKey   = snapshotKey,
            TargetType    = targetType,
        };

        foreach (var (key, value) in resolvedVariables)
            package.ResolvedVariables.Add(key, value);

        return package;
    }

    private sealed record StepExecutionResult
    {
        public required bool Success { get; init; }
        public string? Summary { get; init; }
        public DateTimeOffset? CompletedAt { get; init; }
        public string? ErrorMessage { get; init; }
        public string? ErrorTypeName { get; init; }
    }
}
