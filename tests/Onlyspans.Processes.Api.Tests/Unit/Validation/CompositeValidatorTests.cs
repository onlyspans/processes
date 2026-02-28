using FluentAssertions;
using Onlyspans.Processes.Api.Features.Parsing;
using Onlyspans.Processes.Api.Features.Parsing.Models;
using Onlyspans.Processes.Api.Features.Validation;
using Onlyspans.Processes.Api.Tests.Fixtures;

namespace Onlyspans.Processes.Api.Tests.Unit.Validation;

public sealed class CompositeValidatorTests
{
    private readonly CompositeProcessValidator _validator;
    private readonly YamlProcessDefinitionParser _parser = new();

    public CompositeValidatorTests()
    {
        _validator = new CompositeProcessValidator(
            new ProcessDefinitionStructureValidator(),
            new PipelineGraphValidator());
    }

    [Fact]
    public async Task Validate_ValidSimple_ReturnsValid()
    {
        // Arrange
        var definition = _parser.Parse(YamlFixture.ValidSimple);

        // Act
        var result = await _validator.ValidateAsync(definition);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Validate_ValidFull_ReturnsValid()
    {
        // Arrange
        var definition = _parser.Parse(YamlFixture.ValidFull);

        // Act
        var result = await _validator.ValidateAsync(definition);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Validate_ValidFull_ReportsSecretVariablesAsUnresolved()
    {
        // Arrange
        var definition = _parser.Parse(YamlFixture.ValidFull);

        // Act
        var result = await _validator.ValidateAsync(definition);

        // Assert
        result.UnresolvedVariables.Should().Contain("MATTERMOST_WEBHOOK");
        result.UnresolvedVariables.Should().Contain("DEPLOY_TOKEN");
    }

    [Fact]
    public async Task Validate_ValidFull_ReportsWarningsForUnresolved()
    {
        // Arrange
        var definition = _parser.Parse(YamlFixture.ValidFull);

        // Act
        var result = await _validator.ValidateAsync(definition);

        // Assert
        result.Warnings.Should().ContainSingle(w =>
            w.Contains("external resolution", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validate_NullSteps_FailsEarly()
    {
        // Arrange
        var definition = new ProcessDefinition { Steps = null };

        // Act
        var result = await _validator.ValidateAsync(definition);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Validate_BothScriptAndPath_FailsOnStructure()
    {
        // Arrange
        var definition = _parser.Parse(YamlFixture.InvalidBothScriptAndPath);

        // Act
        var result = await _validator.ValidateAsync(definition);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_ValidWithInlineVariablesOnly_NoUnresolved()
    {
        // Arrange
        var definition = _parser.Parse(YamlFixture.ValidWithVariables);

        // Act
        var result = await _validator.ValidateAsync(definition);

        // Assert
        result.IsValid.Should().BeTrue();
        result.UnresolvedVariables.Should().BeEmpty();
    }
}
