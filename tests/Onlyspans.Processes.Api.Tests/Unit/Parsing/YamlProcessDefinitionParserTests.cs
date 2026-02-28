using FluentAssertions;
using Onlyspans.Processes.Api.Features.Parsing;
using Onlyspans.Processes.Api.Tests.Fixtures;

namespace Onlyspans.Processes.Api.Tests.Unit.Parsing;

public sealed class YamlProcessDefinitionParserTests
{
    private readonly YamlProcessDefinitionParser _parser = new();

    [Fact]
    public void Parse_ValidSimple_ReturnsDefinitionWithSteps()
    {
        // Arrange
        var yaml = YamlFixture.ValidSimple;

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Should().NotBeNull();
        result.Steps.Should().NotBeNull().And.HaveCount(2);
        result.Steps!.Keys.Should().ContainInOrder("build", "deploy");
    }

    [Fact]
    public void Parse_ValidFull_ParsesVariablesCorrectly()
    {
        // Arrange
        var yaml = YamlFixture.ValidFull;

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Variables.Should().NotBeNull().And.HaveCount(5);
        result.Steps.Should().NotBeNull().And.HaveCount(7);

        var inlineVar = result.Variables!.First(v => v.Name == "REPOSITORY_URL");
        inlineVar.Value.Should().Be("https://git.company.ru/data-platform/billing-api.git");
        inlineVar.Source.Should().BeNull();

        var secretVar = result.Variables!.First(v => v.Name == "MATTERMOST_WEBHOOK");
        secretVar.Source.Should().Be("secrets");
        secretVar.Value.Should().BeNull();
    }

    [Fact]
    public void Parse_ValidFull_ParsesStepPropertiesCorrectly()
    {
        // Arrange
        var yaml = YamlFixture.ValidFull;

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        var steps = result.Steps!;

        var checkoutStep = steps["checkout-repo"];
        checkoutStep.Description.Should().Be("Clone the repository");
        checkoutStep.Script.Should().Contain("$REPOSITORY_URL");
        checkoutStep.OnFailure.Should().Be("abort");

        var buildFront = steps["build-front"];
        buildFront.ScriptPath.Should().Be("./scripts/build-front.sh");
        buildFront.Script.Should().BeNull();

        var testStep = steps["run-tests"];
        testStep.Optional.Should().BeTrue();
        testStep.OnFailure.Should().Be("continue");

        var approvalStep = steps["approve-production"];
        approvalStep.Type.Should().Be("approval");
        approvalStep.Timeout.Should().Be("24h");
        approvalStep.Approvers.Should().HaveCount(2);
        approvalStep.Approvers![0].Role.Should().Be("tech-lead");
        approvalStep.Approvers![1].User.Should().Be("ivanov@company.ru");
    }

    [Fact]
    public void Parse_ValidWithVariables_ParsesInlineVariables()
    {
        // Arrange
        var yaml = YamlFixture.ValidWithVariables;

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Variables.Should().HaveCount(2);
        result.Variables![0].Name.Should().Be("REPO_URL");
        result.Variables![0].Value.Should().Be("https://git.example.com/project.git");
        result.Variables![1].Name.Should().Be("IMAGE_TAG");
        result.Variables![1].Value.Should().Be("v1.2.3");
    }

    [Fact]
    public void Parse_ValidWithScriptPath_ParsesScriptPathSteps()
    {
        // Arrange
        var yaml = YamlFixture.ValidWithScriptPath;

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Steps.Should().HaveCount(2);
        result.Steps!["build"].ScriptPath.Should().Be("./scripts/build.sh");
        result.Steps!["build"].Script.Should().BeNull();
        result.Steps!["deploy"].ScriptPath.Should().Be("./scripts/deploy.sh");
    }

    [Fact]
    public void Parse_ValidWithApproval_ParsesApprovalStep()
    {
        // Arrange
        var yaml = YamlFixture.ValidWithApproval;

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        var approveStep = result.Steps!["approve"];
        approveStep.Type.Should().Be("approval");
        approveStep.Approvers.Should().HaveCount(1);
        approveStep.Approvers![0].Role.Should().Be("devops-lead");
    }

    [Fact]
    public void Parse_ValidFull_PreservesStepOrder()
    {
        // Arrange
        var yaml = YamlFixture.ValidFull;

        // Act
        var ordered = _parser.Parse(yaml).GetOrderedSteps();

        // Assert
        ordered.Should().HaveCount(7);
        ordered[0].Name.Should().Be("checkout-repo");
        ordered[0].Order.Should().Be(0);
        ordered[6].Name.Should().Be("send-notification");
        ordered[6].Order.Should().Be(6);
    }

    [Fact]
    public void Parse_NoVariables_VariablesIsNull()
    {
        // Arrange
        var yaml = YamlFixture.ValidSimple;

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        result.Variables.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NullOrWhitespace_ThrowsArgumentException(string? yaml)
    {
        // Arrange & Act
        var act = () => _parser.Parse(yaml!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Parse_InvalidYamlSyntax_ThrowsException()
    {
        // Arrange
        var yaml = YamlFixture.InvalidNotYaml;

        // Act
        var act = () => _parser.Parse(yaml);

        // Assert
        act.Should().Throw<Exception>();
    }
}
