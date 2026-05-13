using System.Globalization;
using Onlyspans.Processes.Api.Data.Entities;
using Worker.Communication;

namespace Onlyspans.Processes.Api.Features.Deployment;

/// <summary>
/// Builds a strict <see cref="StepExecutionMetadata"/> for a single executable
/// <see cref="ProcessStep"/>. Enforces the Worker contract invariants so a malformed
/// step package can never be sent: command type must be set, command source must be
/// exactly one of inline_script / script_path.
/// </summary>
public static class StepPackageBuilder
{
    /// <summary>
    /// All script steps currently map to shell. Other command types
    /// (kubernetes/helm) are not yet expressible in the YAML schema.
    /// </summary>
    private const CommandType DefaultCommandType = CommandType.Shell;

    public static StepExecutionMetadata Build(
        Guid deploymentId,
        DeploymentProcess process,
        ProcessStep step,
        string targetId,
        IReadOnlyDictionary<string, string> resolvedVariables)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);

        var command = BuildCommand(step);

        var metadata = new StepExecutionMetadata
        {
            ExecutionId   = Guid.NewGuid().ToString("N"),
            DeploymentId  = deploymentId.ToString(),
            ProcessId     = process.Id.ToString(),
            StepId        = step.Id.ToString(),
            StepName      = step.Name,
            StepOrder     = step.Order,
            ProjectId     = process.ProjectId.ToString(),
            EnvironmentId = process.EnvironmentId.ToString(),
            TargetId      = targetId,
            Command       = command,
        };

        foreach (var (key, value) in resolvedVariables)
            metadata.ResolvedVariables.Add(key, value);

        return metadata;
    }

    private static StepCommand BuildCommand(ProcessStep step)
    {
        var hasScript     = !string.IsNullOrWhiteSpace(step.Script);
        var hasScriptPath = !string.IsNullOrWhiteSpace(step.ScriptPath);

        if (hasScript && hasScriptPath)
            throw new InvalidOperationException(
                $"Step '{step.Name}' has both inline script and script_path; exactly one is required.");

        if (!hasScript && !hasScriptPath)
            throw new InvalidOperationException(
                $"Step '{step.Name}' has no command source (neither script nor script_path).");

        var command = new StepCommand
        {
            Type             = DefaultCommandType,
            TimeoutSeconds   = ParseTimeoutSeconds(step.Timeout),
            WorkingDirectory = string.Empty,
        };

        if (command.Type == CommandType.Unspecified)
            throw new InvalidOperationException(
                $"Step '{step.Name}' resolved to COMMAND_TYPE_UNSPECIFIED, which is not a valid execution command type.");

        if (hasScript)
            command.InlineScript = step.Script!;
        else
            command.ScriptPath = step.ScriptPath!;

        return command;
    }

    /// <summary>
    /// Parses timeout strings like "30s", "5m", "1h", "2d" or a bare integer (seconds).
    /// Empty / null / unparsable -> 0 (no timeout enforced by Processes; Worker decides default).
    /// </summary>
    public static int ParseTimeoutSeconds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0;

        var trimmed = raw.Trim();

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bareSeconds))
            return bareSeconds < 0 ? 0 : bareSeconds;

        var lastChar = trimmed[^1];
        var numericPart = trimmed[..^1];

        if (!int.TryParse(numericPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
            return 0;

        return char.ToLowerInvariant(lastChar) switch
        {
            's' => value,
            'm' => value * 60,
            'h' => value * 60 * 60,
            'd' => value * 60 * 60 * 24,
            _   => 0,
        };
    }
}
