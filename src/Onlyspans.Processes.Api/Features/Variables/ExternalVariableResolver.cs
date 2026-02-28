using Microsoft.Extensions.Logging;
using Onlyspans.Processes.Api.Features.Parsing.Models;
using Onlyspans.Processes.Api.Grpc.Services;

namespace Onlyspans.Processes.Api.Features.Variables;

/// <summary>
/// Resolves variables in two stages:
/// 1) inline values from YAML definitions;
/// 2) remaining variables via the Variables gRPC service.
/// YAML inline values always take precedence.
/// Variables returned by the service that are absent from YAML
/// are also included — scripts may reference them directly.
/// </summary>
public sealed class ExternalVariableResolver(
    VariablesGrpcService variablesService,
    ILogger<ExternalVariableResolver> logger) : IVariableResolver
{
    public async Task<VariableResolutionResult> ResolveAsync(
        IReadOnlyList<VariableDefinition> variables,
        VariableResolutionContext? context = null,
        CancellationToken ct = default)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var unresolved = new List<string>();

        foreach (var variable in variables)
        {
            if (!string.IsNullOrWhiteSpace(variable.Value)
                && !string.Equals(variable.Source, "secrets", StringComparison.OrdinalIgnoreCase))
            {
                resolved[variable.Name] = variable.Value;
            }
            else
            {
                unresolved.Add(variable.Name);
            }
        }

        if (context?.ProjectId is null)
        {
            return new VariableResolutionResult
            {
                Resolved   = resolved,
                Unresolved = unresolved,
            };
        }

        try
        {
            var result = await variablesService.GetResolvedVariablesAsync(
                context.ProjectId, context.EnvironmentId, ct);

            switch (result)
            {
                case ResolvedVariablesResult.Ok ok:
                {
                    var stillUnresolved = new List<string>();

                    foreach (var name in unresolved)
                    {
                        var match = ok.Variables
                            .FirstOrDefault(v => string.Equals(v.Key, name, StringComparison.OrdinalIgnoreCase));

                        if (match is not null)
                            resolved[name] = match.Value;
                        else
                            stillUnresolved.Add(name);
                    }

                    foreach (var v in ok.Variables)
                        resolved.TryAdd(v.Key, v.Value);

                    unresolved = stillUnresolved;
                    break;
                }

                case ResolvedVariablesResult.Conflict conflict:
                    logger.LogWarning(
                        "Variable conflict from Variables service: key={Key}, sources={Sources}",
                        conflict.Key,
                        string.Join(", ", conflict.ConflictingSources));
                    break;

                case ResolvedVariablesResult.Error error:
                    logger.LogError(
                        "Variables service returned an error: {Message}",
                        error.Message);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve variables via gRPC");
        }

        return new VariableResolutionResult
        {
            Resolved   = resolved,
            Unresolved = unresolved,
        };
    }
}
