using Microsoft.AspNetCore.SignalR;

namespace Onlyspans.Processes.Api.Features.Deployment;

public sealed class DeploymentLogsHub : Hub
{
    public Task Subscribe(string deploymentId)
    {
        return Groups.AddToGroupAsync(
            Context.ConnectionId,
            DeploymentLogRealtime.GroupName(deploymentId));
    }

    public Task Unsubscribe(string deploymentId)
    {
        return Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            DeploymentLogRealtime.GroupName(deploymentId));
    }
}

public static class DeploymentLogRealtime
{
    public const string LogEventName = "deploymentLog";

    public static string GroupName(Guid deploymentId) =>
        GroupName(deploymentId.ToString());

    public static string GroupName(string deploymentId) =>
        $"deployment:{deploymentId}";
}

public sealed record DeploymentLogRealtimeMessage(
    Guid DeploymentId,
    DateTimeOffset Timestamp,
    string Level,
    string Message,
    string? Source);
