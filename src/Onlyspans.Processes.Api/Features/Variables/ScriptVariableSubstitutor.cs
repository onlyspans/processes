using System.Text.RegularExpressions;

namespace Onlyspans.Processes.Api.Features.Variables;

/// <summary>
/// Substitutes $VAR_NAME and ${VAR_NAME} references in scripts with resolved values.
/// </summary>
public static partial class ScriptVariableSubstitutor
{
    public static string Substitute(string script, IReadOnlyDictionary<string, string> variables)
    {
        if (string.IsNullOrWhiteSpace(script) || variables.Count == 0)
            return script;

        return VariablePattern().Replace(script, match =>
        {
            var varName = match.Groups[1].Value;
            return variables.TryGetValue(varName, out var value) ? value : match.Value;
        });
    }

    [GeneratedRegex(@"\$\{?([A-Z_][A-Z0-9_]*)\}?", RegexOptions.Compiled)]
    private static partial Regex VariablePattern();
}
