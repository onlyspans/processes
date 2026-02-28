using FluentAssertions;
using Onlyspans.Processes.Api.Features.Parsing;
using Onlyspans.Processes.Api.Features.Parsing.Models;
using Onlyspans.Processes.Api.Features.Validation;
using Onlyspans.Processes.Api.Tests.Fixtures;

namespace Onlyspans.Processes.Api.Tests.Unit.Validation;

public sealed class PipelineGraphValidatorTests
{
    private readonly PipelineGraphValidator _validator = new();
    private readonly YamlProcessDefinitionParser _parser = new();

    [Fact]
    public async Task Validate_ValidSimple_Passes()
    {
        // Arrange
        var definition = _parser.Parse(YamlFixture.ValidSimple);

        // Act
        var result = await _validator.ValidateAsync(definition);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ValidFull_Passes()
    {
        // Arrange
        var definition = _parser.Parse(YamlFixture.ValidFull);

        // Act
        var result = await _validator.ValidateAsync(definition);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ValidOptionalSteps_Passes()
    {
        // Arrange
        var definition = _parser.Parse(YamlFixture.ValidOptionalSteps);

        // Act
        var result = await _validator.ValidateAsync(definition);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_SingleStep_Passes()
    {
        // Arrange
        var definition = new ProcessDefinition
        {
            Steps = new Dictionary<string, StepDefinition>
            {
                ["only-step"] = new() { Script = "echo hello" },
            },
        };

        // Act
        var result = await _validator.ValidateAsync(definition);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptySteps_ReportsNoStepsDefined()
    {
        // Arrange
        var definition = new ProcessDefinition
        {
            Steps = new Dictionary<string, StepDefinition>(),
        };

        // Act
        var result = await _validator.ValidateAsync(definition);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("No steps defined"));
    }

    [Fact]
    public async Task Validate_ThreeSequentialSteps_AllReachable()
    {
        // Arrange
        var definition = new ProcessDefinition
        {
            Steps = new Dictionary<string, StepDefinition>
            {
                ["step-1"] = new() { Script = "echo 1" },
                ["step-2"] = new() { Script = "echo 2" },
                ["step-3"] = new() { Script = "echo 3" },
            },
        };

        // Act
        var result = await _validator.ValidateAsync(definition);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Validate_OnFailureContinue_StillValid()
    {
        // Arrange
        var definition = new ProcessDefinition
        {
            Steps = new Dictionary<string, StepDefinition>
            {
                ["build"]  = new() { Script = "dotnet build", OnFailure = "continue" },
                ["test"]   = new() { Script = "dotnet test",  OnFailure = "continue" },
                ["deploy"] = new() { Script = "kubectl apply" },
            },
        };

        // Act
        var result = await _validator.ValidateAsync(definition);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_OnFailureRollback_StillValid()
    {
        // Arrange
        var definition = new ProcessDefinition
        {
            Steps = new Dictionary<string, StepDefinition>
            {
                ["deploy"] = new() { Script = "kubectl apply",          OnFailure = "rollback" },
                ["verify"] = new() { Script = "kubectl rollout status" },
            },
        };

        // Act
        var result = await _validator.ValidateAsync(definition);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_MixedOnFailureActions_Passes()
    {
        // Arrange
        var definition = new ProcessDefinition
        {
            Steps = new Dictionary<string, StepDefinition>
            {
                ["build"]  = new() { Script = "build",  OnFailure = "abort" },
                ["test"]   = new() { Script = "test",   OnFailure = "continue", Optional = true },
                ["deploy"] = new() { Script = "deploy", OnFailure = "rollback" },
                ["notify"] = new() { Script = "notify", OnFailure = "continue", Optional = true },
            },
        };

        // Act
        var result = await _validator.ValidateAsync(definition);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ApprovalStepWithScriptSteps_Passes()
    {
        // Arrange
        var definition = _parser.Parse(YamlFixture.ValidWithApproval);

        // Act
        var result = await _validator.ValidateAsync(definition);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}
