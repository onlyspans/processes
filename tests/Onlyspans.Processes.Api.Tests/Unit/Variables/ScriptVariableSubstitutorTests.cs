using FluentAssertions;
using Onlyspans.Processes.Api.Features.Variables;

namespace Onlyspans.Processes.Api.Tests.Unit.Variables;

public sealed class ScriptVariableSubstitutorTests
{
    [Fact]
    public void Substitute_DollarVar_ReplacesWithValue()
    {
        // Arrange
        var vars = new Dictionary<string, string> { ["MY_VAR"] = "hello" };

        // Act
        var result = ScriptVariableSubstitutor.Substitute("echo $MY_VAR", vars);

        // Assert
        result.Should().Be("echo hello");
    }

    [Fact]
    public void Substitute_BracedVar_ReplacesWithValue()
    {
        // Arrange
        var vars = new Dictionary<string, string> { ["MY_VAR"] = "world" };

        // Act
        var result = ScriptVariableSubstitutor.Substitute("echo ${MY_VAR}!", vars);

        // Assert
        result.Should().Be("echo world!");
    }

    [Fact]
    public void Substitute_MultipleVars_ReplacesAll()
    {
        // Arrange
        var vars = new Dictionary<string, string>
        {
            ["REPO"] = "https://git.example.com/repo.git",
            ["TAG"]  = "v1.0.0",
        };
        var script = "git clone $REPO && docker build -t app:${TAG}";

        // Act
        var result = ScriptVariableSubstitutor.Substitute(script, vars);

        // Assert
        result.Should().Be("git clone https://git.example.com/repo.git && docker build -t app:v1.0.0");
    }

    [Fact]
    public void Substitute_UnknownVar_LeavesAsIs()
    {
        // Arrange
        var vars = new Dictionary<string, string> { ["KNOWN"] = "value" };

        // Act
        var result = ScriptVariableSubstitutor.Substitute("$KNOWN $UNKNOWN", vars);

        // Assert
        result.Should().Be("value $UNKNOWN");
    }

    [Fact]
    public void Substitute_EmptyVars_ReturnsOriginal()
    {
        // Arrange
        var vars = new Dictionary<string, string>();

        // Act
        var result = ScriptVariableSubstitutor.Substitute("echo $MY_VAR", vars);

        // Assert
        result.Should().Be("echo $MY_VAR");
    }

    [Fact]
    public void Substitute_NullScript_ReturnsNull()
    {
        // Arrange
        var vars = new Dictionary<string, string> { ["X"] = "y" };

        // Act
        var result = ScriptVariableSubstitutor.Substitute(null!, vars);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Substitute_EmptyScript_ReturnsEmpty()
    {
        // Arrange
        var vars = new Dictionary<string, string> { ["X"] = "y" };

        // Act
        var result = ScriptVariableSubstitutor.Substitute("", vars);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Substitute_NoVarReferences_ReturnsOriginal()
    {
        // Arrange
        var vars = new Dictionary<string, string> { ["X"] = "y" };

        // Act
        var result = ScriptVariableSubstitutor.Substitute("echo hello world", vars);

        // Assert
        result.Should().Be("echo hello world");
    }

    [Fact]
    public void Substitute_CaseInsensitiveKeys_MatchesCorrectly()
    {
        // Arrange
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["my_var"] = "resolved",
        };

        // Act
        var result = ScriptVariableSubstitutor.Substitute("echo $MY_VAR", vars);

        // Assert
        result.Should().Be("echo resolved");
    }

    [Fact]
    public void Substitute_ComplexScript_SubstitutesCorrectly()
    {
        // Arrange
        var vars = new Dictionary<string, string>
        {
            ["REGISTRY"]  = "registry.company.ru",
            ["APP_NAME"]  = "billing-api",
            ["VERSION"]   = "1.2.3",
            ["NAMESPACE"] = "production",
        };
        var script = "docker build -t ${REGISTRY}/${APP_NAME}:$VERSION . && " +
                     "kubectl -n $NAMESPACE apply -f deploy.yaml";

        // Act
        var result = ScriptVariableSubstitutor.Substitute(script, vars);

        // Assert
        result.Should().Be(
            "docker build -t registry.company.ru/billing-api:1.2.3 . && " +
            "kubectl -n production apply -f deploy.yaml");
    }
}
