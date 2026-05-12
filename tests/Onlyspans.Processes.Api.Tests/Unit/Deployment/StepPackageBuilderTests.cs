using FluentAssertions;
using Onlyspans.Processes.Api.Data.Entities;
using Onlyspans.Processes.Api.Domain.Enums;
using Onlyspans.Processes.Api.Features.Deployment;
using Worker.Communication;

namespace Onlyspans.Processes.Api.Tests.Unit.Deployment;

public sealed class StepPackageBuilderTests
{
    private static readonly Guid DeploymentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string TargetId = "target-1";

    [Fact]
    public void Build_InlineScript_MapsToInlineScriptOneof()
    {
        var process = NewProcess();
        var step = NewStep(script: "echo hi");

        var metadata = StepPackageBuilder.Build(
            DeploymentId, process, step, TargetId, new Dictionary<string, string>());

        metadata.Command.SourceCase.Should().Be(StepCommand.SourceOneofCase.InlineScript);
        metadata.Command.InlineScript.Should().Be("echo hi");
        metadata.Command.Type.Should().Be(CommandType.Shell);
    }

    [Fact]
    public void Build_ScriptPath_MapsToScriptPathOneof()
    {
        var process = NewProcess();
        var step = NewStep(scriptPath: "./scripts/run.sh");

        var metadata = StepPackageBuilder.Build(
            DeploymentId, process, step, TargetId, new Dictionary<string, string>());

        metadata.Command.SourceCase.Should().Be(StepCommand.SourceOneofCase.ScriptPath);
        metadata.Command.ScriptPath.Should().Be("./scripts/run.sh");
        metadata.Command.Type.Should().Be(CommandType.Shell);
    }

    [Fact]
    public void Build_NoScriptOrScriptPath_Throws()
    {
        var process = NewProcess();
        var step = NewStep();

        var act = () => StepPackageBuilder.Build(
            DeploymentId, process, step, TargetId, new Dictionary<string, string>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no command source*");
    }

    [Fact]
    public void Build_BothScriptAndScriptPath_Throws()
    {
        var process = NewProcess();
        var step = NewStep(script: "echo hi", scriptPath: "./scripts/run.sh");

        var act = () => StepPackageBuilder.Build(
            DeploymentId, process, step, TargetId, new Dictionary<string, string>());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*both inline script and script_path*");
    }

    [Fact]
    public void Build_PropagatesIdsAndOrder()
    {
        var process = NewProcess();
        var step = NewStep(script: "echo hi");

        var metadata = StepPackageBuilder.Build(
            DeploymentId, process, step, TargetId, new Dictionary<string, string>());

        metadata.DeploymentId.Should().Be(DeploymentId.ToString());
        metadata.ProcessId.Should().Be(process.Id.ToString());
        metadata.ProjectId.Should().Be(process.ProjectId.ToString());
        metadata.EnvironmentId.Should().Be(process.EnvironmentId.ToString());
        metadata.StepId.Should().Be(step.Id.ToString());
        metadata.StepName.Should().Be(step.Name);
        metadata.StepOrder.Should().Be(step.Order);
        metadata.TargetId.Should().Be(TargetId);
    }

    [Fact]
    public void Build_CopiesResolvedVariables()
    {
        var process = NewProcess();
        var step = NewStep(script: "echo hi");
        var vars = new Dictionary<string, string>
        {
            ["FOO"] = "bar",
            ["BAZ"] = "qux",
        };

        var metadata = StepPackageBuilder.Build(
            DeploymentId, process, step, TargetId, vars);

        metadata.ResolvedVariables.Should().BeEquivalentTo(vars);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("0", 0)]
    [InlineData("30", 30)]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("1h", 3600)]
    [InlineData("24h", 24 * 3600)]
    [InlineData("2d", 2 * 24 * 3600)]
    [InlineData("garbage", 0)]
    [InlineData("-5s", 0)]
    public void ParseTimeoutSeconds_HandlesKnownFormats(string? input, int expected)
    {
        StepPackageBuilder.ParseTimeoutSeconds(input).Should().Be(expected);
    }

    private static DeploymentProcess NewProcess() => new()
    {
        Id             = Guid.NewGuid(),
        ProjectId      = Guid.NewGuid(),
        EnvironmentId  = Guid.NewGuid(),
        ReleaseVersion = "1.0.0",
    };

    private static ProcessStep NewStep(string? script = null, string? scriptPath = null) => new()
    {
        Id          = Guid.NewGuid(),
        ProcessId   = Guid.NewGuid(),
        Name        = "step-1",
        Order       = 0,
        Type        = StepType.Script,
        Script      = script,
        ScriptPath  = scriptPath,
        OnFailure   = OnFailureAction.Abort,
        Status      = StepStatus.Pending,
    };
}
