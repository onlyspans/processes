using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Onlyspans.Processes.Api.Features;
using Onlyspans.Processes.Api.Tests.Fixtures;
using static Onlyspans.Processes.Api.Tests.Fixtures.AppFixture;

namespace Onlyspans.Processes.Api.Tests.Integration;

public sealed class ProcessServiceTests(AppFixture appFixture) : IClassFixture<AppFixture>
{
    [Fact]
    public async Task ValidateAsync_ValidSimpleYaml_ReturnsValidWithTwoSteps()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();

        // Act
        var result = await service.ValidateAsync(
            YamlFixture.ValidSimple, ct: cancellationToken);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Steps.Should().NotBeNull().And.HaveCount(2);
    }

    [Fact]
    public async Task ValidateAsync_ValidFullYaml_ReturnsValidWithSevenSteps()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();

        // Act
        var result = await service.ValidateAsync(
            YamlFixture.ValidFull, ct: cancellationToken);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Steps.Should().HaveCount(7);
        result.Steps![0].Name.Should().Be("checkout-repo");
    }

    [Fact]
    public async Task ValidateAsync_ValidFullYaml_SubstitutesInlineVariables()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();

        // Act
        var result = await service.ValidateAsync(
            YamlFixture.ValidFull, ct: cancellationToken);

        // Assert
        result.IsValid.Should().BeTrue();
        var checkoutStep = result.Steps!.First(s => s.Name == "checkout-repo");
        checkoutStep.Script.Should().Contain(
            "https://git.company.ru/data-platform/billing-api.git");
        checkoutStep.Script.Should().NotContain("$REPOSITORY_URL");
    }

    [Fact]
    public async Task ValidateAsync_ValidFullYaml_ReportsScriptPathSteps()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();

        // Act
        var result = await service.ValidateAsync(
            YamlFixture.ValidFull, ct: cancellationToken);

        // Assert
        var buildFront = result.Steps!.First(s => s.Name == "build-front");
        buildFront.ScriptPath.Should().Be("./scripts/build-front.sh");
        buildFront.Script.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_ValidFullYaml_ReportsUnresolvedSecrets()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();

        // Act
        var result = await service.ValidateAsync(
            YamlFixture.ValidFull, ct: cancellationToken);

        // Assert
        result.UnresolvedVariables.Should().Contain("MATTERMOST_WEBHOOK");
        result.UnresolvedVariables.Should().Contain("DEPLOY_TOKEN");
    }

    [Fact]
    public async Task ValidateAsync_InvalidYaml_ReturnsParseError()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();

        // Act
        var result = await service.ValidateAsync(
            YamlFixture.InvalidNotYaml, ct: cancellationToken);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Contains("YAML parse error", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_InvalidNoSteps_ReturnsValidationErrors()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();

        // Act
        var result = await service.ValidateAsync(
            YamlFixture.InvalidNoSteps, ct: cancellationToken);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_VariablesWithValues_SubstitutesCorrectly()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();

        // Act
        var result = await service.ValidateAsync(
            YamlFixture.ValidWithVariables, ct: cancellationToken);

        // Assert
        result.IsValid.Should().BeTrue();

        var cloneStep = result.Steps!.First(s => s.Name == "clone");
        cloneStep.Script.Should().Contain("https://git.example.com/project.git");
        cloneStep.Script.Should().NotContain("$REPO_URL");

        var buildStep = result.Steps!.First(s => s.Name == "build");
        buildStep.Script.Should().Contain("v1.2.3");
    }

    [Fact]
    public async Task CreateAsync_ValidYaml_SavesProcessToDb()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var projectId = Guid.NewGuid();
        var version = "1.0.0";

        // Act
        var result = await service.CreateAsync(
            projectId, Guid.NewGuid(), version, YamlFixture.ValidSimple, ct: cancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBeEmpty();
        result.ProjectId.Should().Be(projectId);
        result.EnvironmentId.Should().NotBeEmpty();
        result.ReleaseVersion.Should().Be(version);
        result.Status.Should().Be("Validated");
        result.StepsCount.Should().Be(2);
    }

    [Fact]
    public async Task CreateAsync_ThenGetById_ReturnsSameProcess()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var projectId = Guid.NewGuid();

        var created = await service.CreateAsync(
            projectId, Guid.NewGuid(), "2.0.0", YamlFixture.ValidSimple, ct: cancellationToken);

        // Act
        var retrieved = await service.GetByIdAsync(created.Id, ct: cancellationToken);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(created.Id);
        retrieved.ProjectId.Should().Be(projectId);
        retrieved.ReleaseVersion.Should().Be("2.0.0");
        retrieved.Steps.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateAsync_FullYaml_SavesAllStepsWithCorrectOrder()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var projectId = Guid.NewGuid();

        // Act
        var result = await service.CreateAsync(
            projectId, Guid.NewGuid(), "3.0.0", YamlFixture.ValidFull, ct: cancellationToken);

        // Assert
        result.Steps.Should().HaveCount(7);
        result.Steps[0].Name.Should().Be("checkout-repo");
        result.Steps[0].Order.Should().Be(0);
        result.Steps[6].Name.Should().Be("send-notification");
        result.Steps[6].Order.Should().Be(6);
    }

    [Fact]
    public async Task CreateAsync_FullYaml_SavesVariables()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var projectId = Guid.NewGuid();

        // Act
        var result = await service.CreateAsync(
            projectId, Guid.NewGuid(), "4.0.0", YamlFixture.ValidFull, ct: cancellationToken);

        // Assert
        result.Variables.Should().HaveCount(5);
        result.Variables.Should().Contain(v => v.Name == "REPOSITORY_URL" && v.HasValue);
        result.Variables.Should().Contain(v =>
            v.Name == "MATTERMOST_WEBHOOK" && v.Source == "Secrets");
    }

    [Fact]
    public async Task CreateAsync_WithInlineVariables_SubstitutesInScripts()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var projectId = Guid.NewGuid();

        // Act
        var result = await service.CreateAsync(
            projectId, Guid.NewGuid(), "5.0.0", YamlFixture.ValidWithVariables, ct: cancellationToken);

        // Assert
        var cloneStep = result.Steps.First(s => s.Name == "clone");
        cloneStep.Script.Should().Contain("https://git.example.com/project.git");
        cloneStep.Script.Should().NotContain("$REPO_URL");
    }

    [Fact]
    public async Task CreateAsync_WithScriptPath_PreservesPathForWorker()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var projectId = Guid.NewGuid();

        // Act
        var result = await service.CreateAsync(
            projectId, Guid.NewGuid(), "6.0.0", YamlFixture.ValidWithScriptPath, ct: cancellationToken);

        // Assert
        var buildStep = result.Steps.First(s => s.Name == "build");
        buildStep.ScriptPath.Should().Be("./scripts/build.sh");
        buildStep.Script.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_InvalidYaml_ThrowsException()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var projectId = Guid.NewGuid();

        // Act
        var act = async () => await service.CreateAsync(
            projectId, Guid.NewGuid(), "0.0.1", YamlFixture.InvalidNoSteps, ct: cancellationToken);

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task GetByIdAsync_NonExistent_ReturnsNull()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();

        // Act
        var result = await service.GetByIdAsync(Guid.NewGuid(), ct: cancellationToken);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ListByProjectAsync_TwoProcessesSaved_ReturnsBoth()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var projectId = Guid.NewGuid();

        var envId = Guid.NewGuid();
        await service.CreateAsync(
            projectId, envId, "1.0.0", YamlFixture.ValidSimple, ct: cancellationToken);
        await service.CreateAsync(
            projectId, envId, "1.1.0", YamlFixture.ValidSimple, ct: cancellationToken);

        // Act
        var results = await service.ListByProjectAsync(projectId, ct: cancellationToken);

        // Assert
        results.Should().HaveCount(2);
        results.Should().OnlyContain(p => p.ProjectId == projectId);
    }

    [Fact]
    public async Task ListByProjectAsync_EmptyProject_ReturnsEmptyList()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();

        // Act
        var results = await service.ListByProjectAsync(
            Guid.NewGuid(), ct: cancellationToken);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ListByProjectAsync_FilterByEnvironmentAndRelease_ReturnsMatchingRow()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var projectId = Guid.NewGuid();
        var envA = Guid.NewGuid();
        var envB = Guid.NewGuid();

        await service.CreateAsync(
            projectId, envA, "2.0.0", YamlFixture.ValidSimple, ct: cancellationToken);
        await service.CreateAsync(
            projectId, envB, "2.0.0", YamlFixture.ValidSimple, ct: cancellationToken);
        await service.CreateAsync(
            projectId, envA, "2.1.0", YamlFixture.ValidSimple, ct: cancellationToken);

        // Act
        var results = await service.ListByProjectAsync(
            projectId,
            environmentId: envA,
            releaseVersion: "2.0.0",
            ct: cancellationToken);

        // Assert
        results.Should().ContainSingle();
        results[0].EnvironmentId.Should().Be(envA);
        results[0].ReleaseVersion.Should().Be("2.0.0");
    }

    [Fact]
    public async Task ListByProjectAsync_FilterByReleaseNotInDb_ReturnsEmpty()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var projectId = Guid.NewGuid();
        var envId = Guid.NewGuid();

        await service.CreateAsync(
            projectId, envId, "1.0.0", YamlFixture.ValidSimple, ct: cancellationToken);

        // Act — версия артефакта / snapshot может существовать без сохранённого процесса
        var results = await service.ListByProjectAsync(
            projectId,
            environmentId: envId,
            releaseVersion: "9.9.9-not-saved",
            ct: cancellationToken);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ListByProjectAsync_FallbackLatestWhenReleaseMissing_ReturnsNewestForEnvironment()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var projectId = Guid.NewGuid();
        var envId = Guid.NewGuid();

        await service.CreateAsync(
            projectId, envId, "1.0.0", YamlFixture.ValidSimple, ct: cancellationToken);
        await service.CreateAsync(
            projectId, envId, "2.0.0", YamlFixture.ValidSimple, ct: cancellationToken);

        // Act
        var results = await service.ListByProjectAsync(
            projectId,
            environmentId: envId,
            releaseVersion: "9.9.9-not-saved",
            fallbackToLatestInEnvironmentWhenReleaseUnmatched: true,
            ct: cancellationToken);

        // Assert — последний по времени создания в этом окружении
        results.Should().ContainSingle();
        results[0].ReleaseVersion.Should().Be("2.0.0");
    }

    [Fact]
    public async Task ListByProjectAsync_FallbackWhenExactMatchExists_ReturnsOnlyExact()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var projectId = Guid.NewGuid();
        var envId = Guid.NewGuid();

        await service.CreateAsync(
            projectId, envId, "1.0.0", YamlFixture.ValidSimple, ct: cancellationToken);
        await service.CreateAsync(
            projectId, envId, "2.0.0", YamlFixture.ValidSimple, ct: cancellationToken);

        // Act
        var results = await service.ListByProjectAsync(
            projectId,
            environmentId: envId,
            releaseVersion: "1.0.0",
            fallbackToLatestInEnvironmentWhenReleaseUnmatched: true,
            ct: cancellationToken);

        // Assert
        results.Should().ContainSingle();
        results[0].ReleaseVersion.Should().Be("1.0.0");
    }

    [Fact]
    public async Task GetByProjectAndVersionAsync_ExistingProcess_ReturnsIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var projectId = Guid.NewGuid();

        await service.CreateAsync(
            projectId, Guid.NewGuid(), "7.0.0", YamlFixture.ValidSimple, ct: cancellationToken);

        // Act
        var result = await service.GetByProjectAndVersionAsync(
            projectId, "7.0.0", ct: cancellationToken);

        // Assert
        result.Should().NotBeNull();
        result!.ReleaseVersion.Should().Be("7.0.0");
    }

    [Fact]
    public async Task GetByProjectAndVersionAsync_NonExistent_ReturnsNull()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();

        // Act
        var result = await service.GetByProjectAndVersionAsync(
            Guid.NewGuid(), "999.0.0", ct: cancellationToken);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_ApprovalStep_SavedWithCorrectType()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var projectId = Guid.NewGuid();

        // Act
        var result = await service.CreateAsync(
            projectId, Guid.NewGuid(), "8.0.0", YamlFixture.ValidWithApproval, ct: cancellationToken);

        // Assert
        var approvalStep = result.Steps.First(s => s.Name == "approve");
        approvalStep.Type.Should().Be("Approval");
    }

    [Fact]
    public async Task CreateAsync_OptionalStep_SavedWithOptionalFlag()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = await CreateSubject();
        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var projectId = Guid.NewGuid();

        // Act
        var result = await service.CreateAsync(
            projectId, Guid.NewGuid(), "9.0.0", YamlFixture.ValidOptionalSteps, ct: cancellationToken);

        // Assert
        var testStep = result.Steps.First(s => s.Name == "test");
        testStep.Optional.Should().BeTrue();

        var buildStep = result.Steps.First(s => s.Name == "build");
        buildStep.Optional.Should().BeFalse();
    }

    private Task<Microsoft.AspNetCore.Builder.WebApplication> CreateSubject(
        ConfigureServices? preconfigure = null,
        ConfigureServices? postconfigure = null)
        => appFixture.BuildApplicationAsync(
            preconfigure: preconfigure,
            postconfigure: postconfigure);
}
