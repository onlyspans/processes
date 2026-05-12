using Grpc.Core;
using Worker.Communication;

namespace Onlyspans.Processes.Api.Grpc.Services;

public class WorkerGrpcService(
    WorkerService.WorkerServiceClient client)
{
    /// <summary>
    /// Opens a bidirectional stream to Worker for executing a single prepared step.
    /// Caller writes <see cref="StepExecutionInput"/> messages (metadata first, then
    /// artifact chunks) and reads back <see cref="StepExecutionMessage"/> log/result.
    /// </summary>
    public virtual AsyncDuplexStreamingCall<StepExecutionInput, StepExecutionMessage> ExecuteStep(
        CancellationToken ct = default)
    {
        return client.ExecuteStep(cancellationToken: ct);
    }
}
