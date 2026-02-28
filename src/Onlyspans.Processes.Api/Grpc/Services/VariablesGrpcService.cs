using Variables.Communication;

namespace Onlyspans.Processes.Api.Grpc.Services;

public sealed class VariablesGrpcService(
    VariablesService.VariablesServiceClient client)
{
    public async Task<ResolvedVariablesResult> GetResolvedVariablesAsync(
        Guid projectId,
        Guid? environmentId,
        CancellationToken ct = default)
    {
        var response = await client.GetResolvedVariablesAsync(
            new GetResolvedVariablesInput
            {
                ProjectId     = projectId.ToString(),
                EnvironmentId = environmentId?.ToString() ?? string.Empty,
            },
            cancellationToken: ct);

        return response.ResultCase switch
        {
            GetResolvedVariablesResult.ResultOneofCase.Success =>
                new ResolvedVariablesResult.Ok(
                    response.Success.Variables
                        .Select(v => new ResolvedVariable(v.Key, v.Value, v.Source))
                        .ToList()),

            GetResolvedVariablesResult.ResultOneofCase.ConflictError =>
                new ResolvedVariablesResult.Conflict(
                    response.ConflictError.Key,
                    response.ConflictError.ConflictingSources.ToList()),

            GetResolvedVariablesResult.ResultOneofCase.InternalError =>
                new ResolvedVariablesResult.Error(
                    response.InternalError.Message,
                    response.InternalError.Trace),

            _ => new ResolvedVariablesResult.Error("Unknown response type", string.Empty),
        };
    }

    public async Task<bool> ValidateProjectExistsAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var response = await client.ValidateProjectExistsAsync(
            new ValidateProjectInput { ProjectId = projectId.ToString() },
            cancellationToken: ct);

        return response.Exists;
    }

    public async Task<bool> ValidateEnvironmentExistsAsync(
        Guid environmentId,
        CancellationToken ct = default)
    {
        var response = await client.ValidateEnvironmentExistsAsync(
            new ValidateEnvironmentInput { EnvironmentId = environmentId.ToString() },
            cancellationToken: ct);

        return response.Exists;
    }
}

public sealed record ResolvedVariable(string Key, string Value, string Source);

public abstract record ResolvedVariablesResult
{
    public sealed record Ok(IReadOnlyList<ResolvedVariable> Variables) : ResolvedVariablesResult;
    public sealed record Conflict(string Key, IReadOnlyList<string> ConflictingSources) : ResolvedVariablesResult;
    public sealed record Error(string Message, string Trace) : ResolvedVariablesResult;
}
