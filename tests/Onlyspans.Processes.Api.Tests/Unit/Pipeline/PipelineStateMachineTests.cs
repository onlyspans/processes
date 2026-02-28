using FluentAssertions;
using Onlyspans.Processes.Api.Features.Parsing.Models;
using Onlyspans.Processes.Api.Features.Pipeline;

namespace Onlyspans.Processes.Api.Tests.Unit.Pipeline;

public sealed class PipelineStateMachineTests
{
    private static List<(string Name, StepDefinition Definition, int Order)> CreateSteps(
        params (string Name, string? OnFailure, bool Optional)[] configs)
    {
        return configs.Select((c, i) => (
            c.Name,
            (StepDefinition)new()
            {
                Script    = $"echo {c.Name}",
                OnFailure = c.OnFailure,
                Optional  = c.Optional,
            },
            i
        )).ToList();
    }

    [Fact]
    public void Constructor_WhenCreated_InitialStateIsNotStarted()
    {
        // Arrange
        var steps = CreateSteps(("build", null, false));

        // Act
        var fsm = new PipelineStateMachine(steps);

        // Assert
        fsm.CurrentState.Should().Be(PipelineStateMachine.StateNotStarted);
        fsm.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void Start_WhenCalled_TransitionsToFirstStep()
    {
        // Arrange
        var steps = CreateSteps(("build", null, false), ("deploy", null, false));
        var fsm = new PipelineStateMachine(steps);

        // Act
        fsm.Start();

        // Assert
        fsm.CurrentState.Should().Be("build");
        fsm.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void StepSucceeded_WhenOnFirstStep_TransitionsToSecondStep()
    {
        // Arrange
        var steps = CreateSteps(("build", null, false), ("deploy", null, false));
        var fsm = new PipelineStateMachine(steps);
        fsm.Start();

        // Act
        fsm.StepSucceeded();

        // Assert
        fsm.CurrentState.Should().Be("deploy");
    }

    [Fact]
    public void StepSucceeded_WhenOnLastStep_TransitionsToCompleted()
    {
        // Arrange
        var steps = CreateSteps(("build", null, false), ("deploy", null, false));
        var fsm = new PipelineStateMachine(steps);
        fsm.Start();
        fsm.StepSucceeded();

        // Act
        fsm.StepSucceeded();

        // Assert
        fsm.CurrentState.Should().Be(PipelineStateMachine.StateCompleted);
        fsm.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void StepFailed_WithAbort_TransitionsToFailed()
    {
        // Arrange
        var steps = CreateSteps(("build", "abort", false), ("deploy", null, false));
        var fsm = new PipelineStateMachine(steps);
        fsm.Start();

        // Act
        fsm.StepFailed();

        // Assert
        fsm.CurrentState.Should().Be(PipelineStateMachine.StateFailed);
        fsm.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void StepFailed_WithDefaultOnFailure_TransitionsToFailed()
    {
        // Arrange
        var steps = CreateSteps(("build", null, false), ("deploy", null, false));
        var fsm = new PipelineStateMachine(steps);
        fsm.Start();

        // Act
        fsm.StepFailed();

        // Assert
        fsm.CurrentState.Should().Be(PipelineStateMachine.StateFailed);
        fsm.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void StepFailed_WithRollback_TransitionsToFailed()
    {
        // Arrange
        var steps = CreateSteps(("deploy", "rollback", false));
        var fsm = new PipelineStateMachine(steps);
        fsm.Start();

        // Act
        fsm.StepFailed();

        // Assert
        fsm.CurrentState.Should().Be(PipelineStateMachine.StateFailed);
    }

    [Fact]
    public void StepFailed_WithContinue_TransitionsToNextStep()
    {
        // Arrange
        var steps = CreateSteps(("test", "continue", false), ("deploy", null, false));
        var fsm = new PipelineStateMachine(steps);
        fsm.Start();

        // Act
        fsm.StepFailed();

        // Assert
        fsm.CurrentState.Should().Be("deploy");
        fsm.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void StepSkipped_OptionalStep_TransitionsToNextStep()
    {
        // Arrange
        var steps = CreateSteps(("test", "abort", true), ("deploy", null, false));
        var fsm = new PipelineStateMachine(steps);
        fsm.Start();

        // Act
        fsm.StepSkipped();

        // Assert
        fsm.CurrentState.Should().Be("deploy");
    }

    [Fact]
    public void StepSkipped_NonOptionalStep_ThrowsInvalidOperation()
    {
        // Arrange
        var steps = CreateSteps(("build", null, false), ("deploy", null, false));
        var fsm = new PipelineStateMachine(steps);
        fsm.Start();

        // Act
        var act = () => fsm.StepSkipped();

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetCurrentStep_WhenOnFirstStep_ReturnsCorrectStep()
    {
        // Arrange
        var steps = CreateSteps(("build", null, false), ("deploy", null, false));
        var fsm = new PipelineStateMachine(steps);
        fsm.Start();

        // Act
        var current = fsm.GetCurrentStep();

        // Assert
        current.Should().NotBeNull();
        current!.Value.Name.Should().Be("build");
    }

    [Fact]
    public void GetCurrentStep_WhenNotStarted_ReturnsNull()
    {
        // Arrange
        var steps = CreateSteps(("build", null, false));
        var fsm = new PipelineStateMachine(steps);

        // Act
        var current = fsm.GetCurrentStep();

        // Assert
        current.Should().BeNull();
    }

    [Fact]
    public void GetCurrentStep_WhenCompleted_ReturnsNull()
    {
        // Arrange
        var steps = CreateSteps(("build", null, false));
        var fsm = new PipelineStateMachine(steps);
        fsm.Start();
        fsm.StepSucceeded();

        // Act
        var current = fsm.GetCurrentStep();

        // Assert
        current.Should().BeNull();
    }

    [Fact]
    public async Task GetPermittedTriggersAsync_AfterStart_ContainsExpectedTriggers()
    {
        // Arrange
        var steps = CreateSteps(("build", "abort", true));
        var fsm = new PipelineStateMachine(steps);
        fsm.Start();

        // Act
        var triggers = (await fsm.GetPermittedTriggersAsync()).ToList();

        // Assert
        triggers.Should().Contain(PipelineStateMachine.Trigger.StepSucceeded);
        triggers.Should().Contain(PipelineStateMachine.Trigger.StepFailed);
        triggers.Should().Contain(PipelineStateMachine.Trigger.StepSkipped);
    }

    [Fact]
    public void FullPipeline_ThreeStepsAllSucceed_ReachesCompleted()
    {
        // Arrange
        var steps = CreateSteps(
            ("build", "abort", false),
            ("test", "continue", true),
            ("deploy", "rollback", false));
        var fsm = new PipelineStateMachine(steps);

        // Act
        fsm.Start();
        fsm.StepSucceeded();
        fsm.StepSucceeded();
        fsm.StepSucceeded();

        // Assert
        fsm.CurrentState.Should().Be(PipelineStateMachine.StateCompleted);
        fsm.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void FullPipeline_SkipOptionalThenDeploy_ReachesCompleted()
    {
        // Arrange
        var steps = CreateSteps(
            ("build", "abort", false),
            ("test", "abort", true),
            ("deploy", null, false));
        var fsm = new PipelineStateMachine(steps);

        // Act
        fsm.Start();
        fsm.StepSucceeded();
        fsm.StepSkipped();
        fsm.StepSucceeded();

        // Assert
        fsm.CurrentState.Should().Be(PipelineStateMachine.StateCompleted);
    }

    [Fact]
    public void FullPipeline_FailWithContinueThenComplete_ReachesCompleted()
    {
        // Arrange
        var steps = CreateSteps(
            ("test", "continue", false),
            ("deploy", null, false));
        var fsm = new PipelineStateMachine(steps);

        // Act
        fsm.Start();
        fsm.StepFailed();
        fsm.StepSucceeded();

        // Assert
        fsm.CurrentState.Should().Be(PipelineStateMachine.StateCompleted);
    }
}
