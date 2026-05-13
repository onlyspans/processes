using Google.Protobuf;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
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
/// Processes is the pipeline FSM, resolves secrets and downloads the snapshot
/// from ArtifactStorage, then opens a Worker duplex stream per executable step
/// to send <see cref="StepExecutionMetadata"/> + <see cref="ArtifactChunk"/>s
/// and stream back <see cref="LogChunk"/>s and a final <see cref="StepExecutionResult"/>.
/// </summary>
public sealed class DeploymentService(
    ProcessesDbContext db,
    WorkerGrpcService workerService,
    ArtifactStorageGrpcService artifactStorageService,
    IVariableResolver variableResolver,
    IDeploymentLogWriter logWriter,
    IHubContext<DeploymentLogsHub> logHub,
    ILogger<DeploymentService> logger)
{
    private const int ArtifactChunkSize = 64 * 1024;
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<DeploymentResponse> ExecuteAsync(
        Guid processId,
        string targetId,
        string targetType,
        string snapshotKey,
        CancellationToken ct = default)
    {
        _ = targetType;

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
        byte[] snapshotBytes;
        try
        {
            snapshotBytes = await ResolveSnapshotBytesAsync(deploymentId, snapshotKey, ct);
        }
        catch (SnapshotResolutionException ex)
        {
            logger.LogError(ex,
                "Failed to resolve snapshot package {SnapshotKey} for deployment {DeploymentId}",
                snapshotKey, deploymentId);

            await logWriter.AppendAsync(deploymentId, new DeploymentLogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level     = "ERROR",
                Message   = ex.Message,
                Source    = "processes",
            }, ct);

            process.Status = ProcessStatus.Failed;
            process.CompletedAt = DateTimeOffset.UtcNow;
            process.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            return new DeploymentResponse
            {
                DeploymentId = deploymentId,
                ProcessId    = processId,
                Status       = ProcessStatus.Failed.ToString(),
                CompletedAt  = process.CompletedAt,
                ErrorMessage = ex.Message,
                ErrorType    = "SnapshotResolutionFailed",
            };
        }

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

            var metadata = StepPackageBuilder.Build(
                deploymentId, process, currentDbStep, targetId, resolvedVariables);

            var stepResult = await ExecuteStepAsync(deploymentId, metadata, snapshotBytes, ct);

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
                deploymentId, orderedSteps, targetId,
                snapshotBytes, resolvedVariables, process, ct);

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
        byte[] snapshotBytes,
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

            var metadata = StepPackageBuilder.Build(
                deploymentId, process, step, targetId, resolvedVariables);

            var result = await ExecuteStepAsync(deploymentId, metadata, snapshotBytes, ct);

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

    private async Task<StepOutcome> ExecuteStepAsync(
        Guid deploymentId,
        StepExecutionMetadata metadata,
        byte[] snapshotBytes,
        CancellationToken ct)
    {
        try
        {
            using var call = workerService.ExecuteStep(ct);

            // Read the response on a background path so HTTP/2 window updates are
            // consumed while large artifact uploads are still being written (avoids
            // write/read flow-control deadlocks on duplex streams).
            var readerTask = Task.Run(
                () => ConsumeStepResponseStreamAsync(deploymentId, call.ResponseStream, ct),
                ct);

            try
            {
                await call.RequestStream.WriteAsync(
                    new StepExecutionInput { Metadata = metadata });

                await StreamArtifactChunksAsync(call.RequestStream, snapshotBytes, ct);

                await call.RequestStream.CompleteAsync();

                return await readerTask.ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    await readerTask.ConfigureAwait(false);
                }
                catch
                {
                    // Prefer the writer-side exception; the reader often surfaces the same RpcException.
                }

                throw;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
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

            return new StepOutcome
            {
                Success       = false,
                ErrorMessage  = ex.Message,
                ErrorTypeName = nameof(ErrorType.Internal),
            };
        }
    }

    private async Task<StepOutcome> ConsumeStepResponseStreamAsync(
        Guid deploymentId,
        global::Grpc.Core.IAsyncStreamReader<StepExecutionMessage> responseStream,
        CancellationToken ct)
    {
        await foreach (var msg in responseStream.ReadAllAsync(ct))
        {
            switch (msg.MessageCase)
            {
                case StepExecutionMessage.MessageOneofCase.Log:
                    await AppendLogAsync(deploymentId, msg.Log, ct);
                    break;

                case StepExecutionMessage.MessageOneofCase.Result:
                    return MapResult(msg.Result);
            }
        }

        return new StepOutcome
        {
            Success      = false,
            ErrorMessage = "Worker stream ended without a result message",
        };
    }

    private static async Task StreamArtifactChunksAsync(
        global::Grpc.Core.IClientStreamWriter<StepExecutionInput> requestStream,
        byte[] snapshotBytes,
        CancellationToken ct)
    {
        if (snapshotBytes.Length == 0)
        {
            await requestStream.WriteAsync(new StepExecutionInput
            {
                ArtifactChunk = new ArtifactChunk
                {
                    Data   = ByteString.Empty,
                    IsLast = true,
                },
            });
            return;
        }

        for (var offset = 0; offset < snapshotBytes.Length; offset += ArtifactChunkSize)
        {
            var len = Math.Min(ArtifactChunkSize, snapshotBytes.Length - offset);
            var isLast = offset + len >= snapshotBytes.Length;

            await requestStream.WriteAsync(new StepExecutionInput
            {
                ArtifactChunk = new ArtifactChunk
                {
                    Data   = ByteString.CopyFrom(snapshotBytes, offset, len),
                    IsLast = isLast,
                },
            });
        }
    }

    private async Task AppendLogAsync(Guid deploymentId, LogChunk log, CancellationToken ct)
    {
        var entry = new DeploymentLogEntry
        {
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(log.Timestamp),
            Level     = log.Level.ToString(),
            Message   = log.Message,
            Source    = log.HasSource ? log.Source : null,
        };

        await logWriter.AppendAsync(deploymentId, entry, ct);
        await logHub.Clients
            .Group(DeploymentLogRealtime.GroupName(deploymentId))
            .SendAsync(
                DeploymentLogRealtime.LogEventName,
                new DeploymentLogRealtimeMessage(
                    deploymentId,
                    entry.Timestamp,
                    entry.Level,
                    entry.Message,
                    entry.Source),
                ct);
    }

    private static StepOutcome MapResult(StepExecutionResult result)
    {
        return result.ResultCase switch
        {
            StepExecutionResult.ResultOneofCase.Success => new StepOutcome
            {
                Success     = true,
                Summary     = result.Success.Summary,
                CompletedAt = DateTimeOffset.FromUnixTimeMilliseconds(result.Success.CompletedAt),
            },

            StepExecutionResult.ResultOneofCase.Error => new StepOutcome
            {
                Success       = false,
                ErrorMessage  = result.Error.Message,
                ErrorTypeName = result.Error.ErrorType.ToString(),
            },

            _ => new StepOutcome
            {
                Success      = false,
                ErrorMessage = "Unknown result type from Worker",
            },
        };
    }

    /// <summary>
    /// Downloads snapshot bytes from ArtifactStorage and resolves a JSON snapshot manifest
    /// to the source artifact bytes expected by Worker/agent-shell.
    /// </summary>
    private async Task<byte[]> ResolveSnapshotBytesAsync(
        Guid deploymentId,
        string snapshotKey,
        CancellationToken ct)
    {
        logger.LogInformation(
            "Downloading snapshot {SnapshotKey} from ArtifactStorage for deployment {DeploymentId}",
            snapshotKey, deploymentId);

        var parts = snapshotKey.Split('@', 2);
        var key = parts[0];
        var version = parts.Length > 1 ? parts[1] : snapshotKey;

        var result = await artifactStorageService.DownloadSnapshotAsync(key, version, ct);
        var snapshot = result switch
        {
            SnapshotResult.Ok ok => ok,

            SnapshotResult.NotFound notFound => throw new SnapshotResolutionException(
                $"Snapshot '{notFound.Key}@{notFound.Version}' not found in ArtifactStorage: {notFound.Message}"),

            SnapshotResult.Error error => throw new SnapshotResolutionException(
                $"Failed to download snapshot from ArtifactStorage: {error.Message}"),

            _ => throw new SnapshotResolutionException("Unexpected response from ArtifactStorage"),
        };

        if (!LooksLikeJsonManifest(snapshot))
            return snapshot.Content;

        logger.LogInformation(
            "Snapshot {SnapshotKey}@{SnapshotVersion} is a JSON manifest; resolving source artifact",
            snapshot.Key, snapshot.Version);

        var manifest = ParseSnapshotManifest(snapshot);
        var sourceArtifact = manifest.SourceArtifact;
        if (string.IsNullOrWhiteSpace(sourceArtifact?.Key) ||
            string.IsNullOrWhiteSpace(sourceArtifact.Version))
        {
            throw new SnapshotResolutionException(
                $"Snapshot manifest '{snapshot.Key}@{snapshot.Version}' must contain sourceArtifact.key and sourceArtifact.version");
        }

        var artifactResult = await artifactStorageService.DownloadArtifactAsync(
            sourceArtifact.Key, sourceArtifact.Version, ct);
        var artifact = artifactResult switch
        {
            SnapshotResult.Ok ok => ok,

            SnapshotResult.NotFound notFound => throw new SnapshotResolutionException(
                $"Source artifact '{notFound.Key}@{notFound.Version}' referenced by snapshot manifest was not found in ArtifactStorage: {notFound.Message}"),

            SnapshotResult.Error error => throw new SnapshotResolutionException(
                $"Failed to download source artifact '{sourceArtifact.Key}@{sourceArtifact.Version}' from ArtifactStorage: {error.Message}"),

            _ => throw new SnapshotResolutionException("Unexpected artifact response from ArtifactStorage"),
        };

        if (artifact.Content.Length == 0)
        {
            throw new SnapshotResolutionException(
                $"Source artifact '{artifact.Key}@{artifact.Version}' referenced by snapshot manifest is empty");
        }

        return artifact.Content;
    }

    private static bool LooksLikeJsonManifest(SnapshotResult.Ok snapshot)
    {
        if (snapshot.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var value in snapshot.Content)
        {
            if (!char.IsWhiteSpace((char)value))
                return value == (byte)'{';
        }

        return false;
    }

    private static SnapshotManifest ParseSnapshotManifest(SnapshotResult.Ok snapshot)
    {
        try
        {
            return JsonSerializer.Deserialize<SnapshotManifest>(
                Encoding.UTF8.GetString(snapshot.Content),
                ManifestJsonOptions)
                ?? throw new SnapshotResolutionException(
                    $"Snapshot manifest '{snapshot.Key}@{snapshot.Version}' is empty");
        }
        catch (JsonException ex)
        {
            throw new SnapshotResolutionException(
                $"Snapshot manifest '{snapshot.Key}@{snapshot.Version}' is not valid JSON: {ex.Message}",
                ex);
        }
    }

    private sealed record SnapshotManifest(SourceArtifactRef? SourceArtifact);

    private sealed record SourceArtifactRef(string? Key, string? Version);

    private sealed class SnapshotResolutionException : Exception
    {
        public SnapshotResolutionException(string message) : base(message) { }

        public SnapshotResolutionException(string message, Exception innerException)
            : base(message, innerException) { }
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

    private sealed record StepOutcome
    {
        public required bool Success { get; init; }
        public string? Summary { get; init; }
        public DateTimeOffset? CompletedAt { get; init; }
        public string? ErrorMessage { get; init; }
        public string? ErrorTypeName { get; init; }
    }
}
