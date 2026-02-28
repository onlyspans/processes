using Grpc.Core;
using Worker.Communication;

namespace Onlyspans.Processes.Api.Grpc.Services;

public class WorkerGrpcService(
    WorkerService.WorkerServiceClient client)
{
    public virtual AsyncServerStreamingCall<DeploymentMessage> ExecuteDeployment(
        DeploymentPackage package,
        CancellationToken ct = default)
    {
        return client.ExecuteDeployment(
            package,
            cancellationToken: ct);
    }
}
