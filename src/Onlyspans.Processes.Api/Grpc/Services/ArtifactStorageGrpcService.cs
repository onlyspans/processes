using ArtifactStorage.Communication;

namespace Onlyspans.Processes.Api.Grpc.Services;

public class ArtifactStorageGrpcService(
    ArtifactStorageService.ArtifactStorageServiceClient client)
{
    public virtual async Task<SnapshotResult> GetSnapshotAsync(
        string snapshotKey,
        CancellationToken ct = default)
    {
        var response = await client.GetSnapshotAsync(
            new GetSnapshotRequest { SnapshotKey = snapshotKey },
            cancellationToken: ct);

        return response.ResultCase switch
        {
            GetSnapshotResponse.ResultOneofCase.Success =>
                new SnapshotResult.Ok(
                    response.Success.SnapshotKey,
                    response.Success.Content.ToByteArray(),
                    response.Success.ContentType,
                    response.Success.SizeBytes,
                    DateTimeOffset.FromUnixTimeMilliseconds(response.Success.CreatedAt)),

            GetSnapshotResponse.ResultOneofCase.NotFound =>
                new SnapshotResult.NotFound(
                    response.NotFound.SnapshotKey,
                    response.NotFound.Message),

            GetSnapshotResponse.ResultOneofCase.InternalError =>
                new SnapshotResult.Error(
                    response.InternalError.Message,
                    response.InternalError.HasTrace ? response.InternalError.Trace : null),

            _ => new SnapshotResult.Error("Unknown response type from ArtifactStorage", null),
        };
    }
}

public abstract record SnapshotResult
{
    public sealed record Ok(
        string SnapshotKey,
        byte[] Content,
        string ContentType,
        long SizeBytes,
        DateTimeOffset CreatedAt) : SnapshotResult;

    public sealed record NotFound(
        string SnapshotKey,
        string Message) : SnapshotResult;

    public sealed record Error(
        string Message,
        string? Trace) : SnapshotResult;
}
