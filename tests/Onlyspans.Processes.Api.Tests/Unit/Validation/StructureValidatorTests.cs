using FluentAssertions;
using Onlyspans.Processes.Api.Features.Parsing;
using Onlyspans.Processes.Api.Features.Parsing.Models;
using Onlyspans.Processes.Api.Features.Validation;
using Onlyspans.Processes.Api.Tests.Fixtures;

namespace Onlyspans.Processes.Api.Tests.Unit.Validation;

public sealed class StructureValidatorTests
{
    private readonly ProcessDefinitionStructureValidator _validator = new();
    private readonly YamlProcessDefinitionParser _parser = new();

    [Fact]
    public void Validate_ValidSimple_Passes()
    {
        // Arrange
        var definition = _parser.Parse(YamlFixture.ValidSimple);

        // Act
        var result = _validator.Validate(definition);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidFull_Passes()
    {
        // Arrange
        var definition = _parser.Parse(YamlFixture.ValidFull);

        // Act
        var result = _validator.Validate(definition);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidWithApproval_Passes()
    {
        // Arrange
        var definition = _parser.Parse(YamlFixture.ValidWithApproval);

        // Act
        var result = _validator.Validate(definition);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidOptionalSteps_Passes()
    {
        // Arrange
        var definition = _parser.Parse(YamlFixture.ValidOptionalSteps);

        // Act
        var result = _validator.Validate(definition);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_NoSteps_Fails()
    {
        // Arrange
        var definition = _parser.Parse(YamlFixture.InvalidNoSteps);

        // Act
        var result = _validator.Validate(definition);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.ErrorMessage.Contains("steps", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_NullSteps_Fails()
    {
        // Arrange
        var definition = new ProcessDefinition { Steps = null };

        // Act
        var result = _validator.Validate(definition);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyStepsDictionary_Fails()
    {
        // Arrange
        var definition = new ProcessDefinition
        {
            Steps = new Dictionary<string, StepDefinition>(),
        };

        // Act
        var result = _validator.Validate(definition);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.ErrorMessage.Contains("at least one step", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_BothScriptAndPath_Fails()
    {
        // Arrange
        var definition = _parser.Parse(YamlFixture.InvalidBothScriptAndPath);

        // Act
        var result = _validator.Validate(definition);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.ErrorMessage.Contains("cannot have both", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_NoScriptNoPath_Fails()
    {
        // Arrange
        var definition = _parser.Parse(YamlFixture.InvalidNoScriptNoPath);

        // Act
        var result = _validator.Validate(definition);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.ErrorMessage.Contains("script", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_DuplicateVariables_Fails()
    {
        // Arrange
        var definition = _parser.Parse(YamlFixture.InvalidDuplicateVariables);

        // Act
        var result = _validator.Validate(definition);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.ErrorMessage.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ApprovalNoApprovers_Fails()
    {
        // Arrange
        var definition = _parser.Parse(YamlFixture.InvalidApprovalNoApprovers);

        // Act
        var result = _validator.Validate(definition);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.ErrorMessage.Contains("approver", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_UnknownStepType_Fails()
    {
        // Arrange
        var definition = _parser.Parse(YamlFixture.InvalidUnknownType);

        // Act
        var result = _validator.Validate(definition);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.ErrorMessage.Contains("Unknown step type", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_VariableWithoutValueOrSource_Fails()
    {
        // Arrange
        var definition = _parser.Parse(YamlFixture.InvalidVariableNoValueNoSource);

        // Act
        var result = _validator.Validate(definition);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.ErrorMessage.Contains("value", StringComparison.OrdinalIgnoreCase)
            || e.ErrorMessage.Contains("source", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_UnknownOnFailureAction_Fails()
    {
        // Arrange
        var definition = new ProcessDefinition
        {
            Steps = new Dictionary<string, StepDefinition>
            {
                ["build"] = new()
                {
                    Script = "dotnet build",
                    OnFailure = "explode",
                },
            },
        };

        // Act
        var result = _validator.Validate(definition);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.ErrorMessage.Contains("Unknown on_failure", StringComparison.OrdinalIgnoreCase));
    }
}
