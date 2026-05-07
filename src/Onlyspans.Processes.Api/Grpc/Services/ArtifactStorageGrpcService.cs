using ArtifactStorage.Communication;
using Grpc.Core;

namespace Onlyspans.Processes.Api.Grpc.Services;

public class ArtifactStorageGrpcService(
    ArtifactStorageService.ArtifactStorageServiceClient client)
{
    /// <summary>
    /// Downloads a snapshot via server-streaming, returning metadata + full content bytes.
    /// </summary>
    public virtual async Task<SnapshotResult> DownloadSnapshotAsync(
        string key,
        string version,
        CancellationToken ct = default)
    {
        try
        {
            using var call = client.DownloadSnapshot(
                new DownloadSnapshotRequest { Key = key, Version = version },
                cancellationToken: ct);

            SnapshotInfo? header = null;
            using var content = new MemoryStream();

            await foreach (var msg in call.ResponseStream.ReadAllAsync(ct))
            {
                switch (msg.PayloadCase)
                {
                    case DownloadSnapshotResponse.PayloadOneofCase.Header:
                        header = msg.Header;
                        break;
                    case DownloadSnapshotResponse.PayloadOneofCase.Chunk:
                        msg.Chunk.WriteTo(content);
                        break;
                }
            }

            if (header is null)
                return new SnapshotResult.Error("No header received from ArtifactStorage", null);

            return new SnapshotResult.Ok(
                header.Key,
                header.Version,
                content.ToArray(),
                header.ContentType,
                header.SizeBytes,
                header.ChecksumSha256,
                header.CreatedAt.ToDateTimeOffset());
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return new SnapshotResult.NotFound(key, version, ex.Status.Detail);
        }
        catch (RpcException ex)
        {
            return new SnapshotResult.Error(ex.Status.Detail, ex.StackTrace);
        }
    }

    /// <summary>
    /// Gets snapshot metadata without downloading content.
    /// </summary>
    public virtual async Task<SnapshotResult> GetSnapshotInfoAsync(
        string key,
        string version,
        CancellationToken ct = default)
    {
        var response = await client.GetSnapshotInfoAsync(
            new GetSnapshotInfoRequest { Key = key, Version = version },
            cancellationToken: ct);

        return response.ResultCase switch
        {
            GetSnapshotInfoResponse.ResultOneofCase.Success =>
                new SnapshotResult.Ok(
                    response.Success.Key,
                    response.Success.Version,
                    [],
                    response.Success.ContentType,
                    response.Success.SizeBytes,
                    response.Success.ChecksumSha256,
                    response.Success.CreatedAt.ToDateTimeOffset()),

            GetSnapshotInfoResponse.ResultOneofCase.Error =>
                response.Error.Code == "NOT_FOUND"
                    ? new SnapshotResult.NotFound(key, version, response.Error.Message)
                    : new SnapshotResult.Error(response.Error.Message,
                        response.Error.HasTrace ? response.Error.Trace : null),

            _ => new SnapshotResult.Error("Unknown response type from ArtifactStorage", null),
        };
    }
}

public abstract record SnapshotResult
{
    public sealed record Ok(
        string Key,
        string Version,
        byte[] Content,
        string ContentType,
        long SizeBytes,
        string ChecksumSha256,
        DateTimeOffset CreatedAt) : SnapshotResult;

    public sealed record NotFound(
        string Key,
        string Version,
        string Message) : SnapshotResult;

    public sealed record Error(
        string Message,
        string? Trace) : SnapshotResult;
}
