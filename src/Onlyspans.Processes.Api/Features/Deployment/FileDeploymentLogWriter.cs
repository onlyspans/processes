using System.Globalization;
using System.Text.Json;

namespace Onlyspans.Processes.Api.Features.Deployment;

/// <summary>
/// Append-only log writer that stores deployment logs as JSONL files.
/// Follows the GitLab model from target-arch.md (ADR-1).
/// </summary>
public sealed class FileDeploymentLogWriter : IDeploymentLogWriter
{
    private readonly string _basePath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public FileDeploymentLogWriter(string basePath)
    {
        _basePath = basePath;
        Directory.CreateDirectory(_basePath);
    }

    public async Task AppendAsync(
        Guid deploymentId,
        DeploymentLogEntry entry,
        CancellationToken ct = default)
    {
        var filePath = GetLogFilePath(deploymentId);
        var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;

        await File.AppendAllTextAsync(filePath, line, ct);
    }

    public async Task<IReadOnlyList<DeploymentLogEntry>> ReadAsync(
        Guid deploymentId,
        CancellationToken ct = default)
    {
        var filePath = GetLogFilePath(deploymentId);

        if (!File.Exists(filePath))
            return [];

        var lines = await File.ReadAllLinesAsync(filePath, ct);
        return ParseLines(lines);
    }

    public async Task<IReadOnlyList<DeploymentLogEntry>> ReadFromOffsetAsync(
        Guid deploymentId,
        long offsetBytes,
        CancellationToken ct = default)
    {
        var filePath = GetLogFilePath(deploymentId);

        if (!File.Exists(filePath))
            return [];

        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (offsetBytes >= fs.Length)
            return [];

        fs.Seek(offsetBytes, SeekOrigin.Begin);
        using var reader = new StreamReader(fs);
        var remaining = await reader.ReadToEndAsync(ct);
        var lines = remaining.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        return ParseLines(lines);
    }

    private string GetLogFilePath(Guid deploymentId) =>
        Path.Combine(_basePath, $"{deploymentId}.jsonl");

    private static IReadOnlyList<DeploymentLogEntry> ParseLines(string[] lines) =>
        lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => JsonSerializer.Deserialize<DeploymentLogEntry>(l, JsonOptions)!)
            .ToList();
}
