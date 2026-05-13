using ArtifactStorage.Communication;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using System.Text;
using Onlyspans.Processes.Api.Features;
using Onlyspans.Processes.Api.Features.Deployment;
using Onlyspans.Processes.Api.Grpc.Services;
using Onlyspans.Processes.Api.Tests.Fixtures;
using Worker.Communication;
using static Onlyspans.Processes.Api.Tests.Fixtures.AppFixture;

namespace Onlyspans.Processes.Api.Tests.Integration;

public sealed class DeploymentServiceTests(AppFixture appFixture) : IClassFixture<AppFixture>
{
    private static readonly Guid EnvironmentId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private const string TargetId = "staging-cluster-1";
    private const string TargetType = "kubernetes";
    private const string SnapshotKey = "snapshots/project-abc/v1.0.0.tar.gz";
    private static readonly byte[] DefaultSnapshotBytes = [0x01, 0x02, 0x03, 0x04];

    [Fact]
    public async Task ExecuteAsync_AllStepsSucceed_ProcessMarkedAsCompleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (mockWorker, _) = CreateMockWorker(SuccessMessages("All steps completed"));

        await using var app = await CreateSubject(postconfigure: builder =>
        {
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockWorker));
        });

        using var scope = app.Services.CreateScope();
        var processService = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var deploymentService = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        var projectId = Guid.NewGuid();
        var created = await processService.CreateAsync(
            projectId, EnvironmentId, "1.0.0", YamlFixture.ValidSimple, ct: cancellationToken);

        var result = await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        result.Status.Should().Be("Completed");
        result.ProcessId.Should().Be(created.Id);
        result.DeploymentId.Should().NotBeEmpty();
        result.Summary.Should().Be("All steps completed");
        result.CompletedAt.Should().NotBeNull();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_AllStepsSucceed_StepsMarkedAsSucceededInDb()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (mockWorker, _) = CreateMockWorker(SuccessMessages("Done"));

        await using var app = await CreateSubject(postconfigure: builder =>
        {
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockWorker));
        });

        using var scope = app.Services.CreateScope();
        var processService = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var deploymentService = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        var projectId = Guid.NewGuid();
        var created = await processService.CreateAsync(
            projectId, EnvironmentId, "2.0.0", YamlFixture.ValidSimple, ct: cancellationToken);

        await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        var process = await processService.GetByIdAsync(created.Id, ct: cancellationToken);
        process.Should().NotBeNull();
        process!.Steps.Should().OnlyContain(s => s.Status == "Succeeded");
    }

    [Fact]
    public async Task ExecuteAsync_StepFails_ProcessMarkedAsFailed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (mockWorker, _) = CreateMockWorker(
            ErrorMessages("Build failed", ErrorType.TargetExecutionFailed));

        await using var app = await CreateSubject(postconfigure: builder =>
        {
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockWorker));
        });

        using var scope = app.Services.CreateScope();
        var processService = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var deploymentService = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        var projectId = Guid.NewGuid();
        var created = await processService.CreateAsync(
            projectId, EnvironmentId, "3.0.0", YamlFixture.ValidSimple, ct: cancellationToken);

        var result = await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        result.Status.Should().Be("Failed");
        result.ErrorMessage.Should().Be("Build failed");
        result.ErrorType.Should().Contain("TargetExecutionFailed");
    }

    [Fact]
    public async Task ExecuteAsync_StepFailsWithContinue_ProcessMarkedAsCompleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (mockWorker, _) = CreateMockWorker(
            ErrorMessages("risky failed", ErrorType.TargetExecutionFailed),
            SuccessMessages("cleanup done"));

        await using var app = await CreateSubject(postconfigure: builder =>
        {
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockWorker));
        });

        using var scope = app.Services.CreateScope();
        var processService = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var deploymentService = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        var projectId = Guid.NewGuid();
        var created = await processService.CreateAsync(
            projectId, EnvironmentId, "8.0.0",
            YamlFixture.ValidContinueThenSucceed, ct: cancellationToken);

        var result = await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        result.Status.Should().Be("Completed");

        var process = await processService.GetByIdAsync(created.Id, ct: cancellationToken);
        process.Should().NotBeNull();
        process!.Steps.Should().ContainSingle(s => s.Name == "risky")
            .Which.Status.Should().Be("Failed");
        process.Steps.Should().ContainSingle(s => s.Name == "cleanup")
            .Which.Status.Should().Be("Succeeded");
    }

    [Fact]
    public async Task ExecuteAsync_StepFailsWithRollback_ProcessMarkedAsRolledBack()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (mockWorker, _) = CreateMockWorker(
            SuccessMessages("staging ok"),
            ErrorMessages("prod failed", ErrorType.TargetExecutionFailed),
            SuccessMessages("staging rolled back"));

        await using var app = await CreateSubject(postconfigure: builder =>
        {
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockWorker));
        });

        using var scope = app.Services.CreateScope();
        var processService = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var deploymentService = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        var projectId = Guid.NewGuid();
        var created = await processService.CreateAsync(
            projectId, EnvironmentId, "9.0.0",
            YamlFixture.ValidRollback, ct: cancellationToken);

        var result = await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        result.Status.Should().Be("RolledBack");

        var process = await processService.GetByIdAsync(created.Id, ct: cancellationToken);
        process.Should().NotBeNull();
        process!.Steps.Should().ContainSingle(s => s.Name == "deploy-staging")
            .Which.Status.Should().Be("Succeeded");
        process.Steps.Should().ContainSingle(s => s.Name == "deploy-prod")
            .Which.Status.Should().Be("Failed");
    }

    [Fact]
    public async Task ExecuteAsync_WorkerStreamsLogs_LogsWrittenToFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var responses = new List<StepExecutionMessage>
        {
            CreateLogMessage("Starting build...", LogLevel.Info),
            CreateLogMessage("Compiling source...", LogLevel.Info),
            CreateLogMessage("Build complete", LogLevel.Info),
            CreateSuccessResultMessage("Build succeeded"),
        };

        var (mockWorker, _) = CreateMockWorker(responses);

        await using var app = await CreateSubject(postconfigure: builder =>
        {
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockWorker));
        });

        using var scope = app.Services.CreateScope();
        var processService = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var deploymentService = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        var projectId = Guid.NewGuid();
        var created = await processService.CreateAsync(
            projectId, EnvironmentId, "4.0.0", YamlFixture.ValidSimple, ct: cancellationToken);

        var result = await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        var logs = await deploymentService.GetLogsAsync(result.DeploymentId, ct: cancellationToken);

        logs.DeploymentId.Should().Be(result.DeploymentId);
        logs.Entries.Should().HaveCountGreaterOrEqualTo(3);
        logs.Entries.Should().Contain(e => e.Message == "Starting build...");
        logs.Entries.Should().Contain(e => e.Message == "Compiling source...");
        logs.Entries.Should().Contain(e => e.Message == "Build complete");
    }

    [Fact]
    public async Task ExecuteAsync_ApprovalStep_ReturnsAwaitingApproval()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (mockWorker, _) = CreateMockWorker(SuccessMessages("Done"));

        await using var app = await CreateSubject(postconfigure: builder =>
        {
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockWorker));
        });

        using var scope = app.Services.CreateScope();
        var processService = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var deploymentService = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        var projectId = Guid.NewGuid();
        var created = await processService.CreateAsync(
            projectId, EnvironmentId, "5.0.0", YamlFixture.ValidWithApproval, ct: cancellationToken);

        var result = await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        result.Status.Should().Be("AwaitingApproval");
    }

    [Fact]
    public async Task ExecuteAsync_NonExistentProcess_ThrowsException()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (mockWorker, _) = CreateMockWorker(SuccessMessages("Done"));

        await using var app = await CreateSubject(postconfigure: builder =>
        {
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockWorker));
        });

        using var scope = app.Services.CreateScope();
        var deploymentService = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        var act = async () => await deploymentService.ExecuteAsync(
            Guid.NewGuid(), TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task ExecuteAsync_WorkerThrowsException_ProcessMarkedAsFailed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mockWorker = Substitute.For<WorkerGrpcService>(
            Substitute.For<WorkerService.WorkerServiceClient>());
        mockWorker
            .ExecuteStep(Arg.Any<CancellationToken>())
            .Returns(_ => throw new global::Grpc.Core.RpcException(
                new global::Grpc.Core.Status(global::Grpc.Core.StatusCode.Unavailable, "Worker unreachable")));

        await using var app = await CreateSubject(postconfigure: builder =>
        {
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockWorker));
        });

        using var scope = app.Services.CreateScope();
        var processService = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var deploymentService = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        var projectId = Guid.NewGuid();
        var created = await processService.CreateAsync(
            projectId, EnvironmentId, "6.0.0", YamlFixture.ValidSimple, ct: cancellationToken);

        var result = await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        result.Status.Should().Be("Failed");
        result.ErrorMessage.Should().Contain("Worker unreachable");
    }

    [Fact]
    public async Task ExecuteAsync_WorkerThrowsException_ErrorLogWritten()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var mockWorker = Substitute.For<WorkerGrpcService>(
            Substitute.For<WorkerService.WorkerServiceClient>());
        mockWorker
            .ExecuteStep(Arg.Any<CancellationToken>())
            .Returns(_ => throw new global::Grpc.Core.RpcException(
                new global::Grpc.Core.Status(global::Grpc.Core.StatusCode.Internal, "Internal error")));

        await using var app = await CreateSubject(postconfigure: builder =>
        {
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockWorker));
        });

        using var scope = app.Services.CreateScope();
        var processService = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var deploymentService = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        var projectId = Guid.NewGuid();
        var created = await processService.CreateAsync(
            projectId, EnvironmentId, "7.0.0", YamlFixture.ValidSimple, ct: cancellationToken);

        var result = await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        var logs = await deploymentService.GetLogsAsync(result.DeploymentId, ct: cancellationToken);

        logs.Entries.Should().Contain(e =>
            e.Level == "ERROR" && e.Source == "processes");
    }

    [Fact]
    public async Task GetLogsAsync_NoLogs_ReturnsEmptyEntries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (mockWorker, _) = CreateMockWorker(SuccessMessages("Done"));

        await using var app = await CreateSubject(postconfigure: builder =>
        {
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockWorker));
        });

        using var scope = app.Services.CreateScope();
        var deploymentService = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        var logs = await deploymentService.GetLogsAsync(Guid.NewGuid(), ct: cancellationToken);

        logs.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_MetadataFirst_ThenArtifactChunks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (mockWorker, recorders) = CreateMockWorker(SuccessMessages("ok"));

        await using var app = await CreateSubject(postconfigure: builder =>
        {
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockWorker));
        });

        using var scope = app.Services.CreateScope();
        var processService = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var deploymentService = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        var projectId = Guid.NewGuid();
        var created = await processService.CreateAsync(
            projectId, EnvironmentId, "10.0.0", YamlFixture.ValidSimple, ct: cancellationToken);

        await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        recorders.Should().NotBeEmpty();
        var first = recorders[0].Written;
        first.Should().NotBeEmpty();
        first[0].InputCase.Should().Be(StepExecutionInput.InputOneofCase.Metadata);

        first.Skip(1).Should().OnlyContain(
            x => x.InputCase == StepExecutionInput.InputOneofCase.ArtifactChunk);

        recorders[0].Completed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_StreamsArtifactChunks_LastFlaggedAndConcatMatchesSnapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (mockWorker, recorders) = CreateMockWorker(SuccessMessages("ok"));

        var snapshotPayload = new byte[150 * 1024];
        for (var i = 0; i < snapshotPayload.Length; i++)
            snapshotPayload[i] = (byte)(i % 251);

        await using var app = await CreateSubject(postconfigure: builder =>
        {
            var mockArtifactStorage = CreateMockArtifactStorage(snapshotPayload);
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockArtifactStorage));
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockWorker));
        });

        using var scope = app.Services.CreateScope();
        var processService = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var deploymentService = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        var projectId = Guid.NewGuid();
        var created = await processService.CreateAsync(
            projectId, EnvironmentId, "11.0.0", YamlFixture.ValidSimple, ct: cancellationToken);

        await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        var firstCall = recorders[0].Written;
        var chunks = firstCall.Skip(1)
            .Select(x => x.ArtifactChunk)
            .ToList();

        chunks.Should().NotBeEmpty();
        chunks.SkipLast(1).Should().OnlyContain(c => !c.IsLast);
        chunks[^1].IsLast.Should().BeTrue();

        var reassembled = chunks.SelectMany(c => c.Data.ToByteArray()).ToArray();
        reassembled.Should().Equal(snapshotPayload);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSnapshotIsManifest_StreamsSourceArtifactBytesToWorker()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (mockWorker, recorders) = CreateMockWorker(SuccessMessages("ok"));

        var manifestBytes = Encoding.UTF8.GetBytes("""
            {
              "sourceArtifact": {
                "key": "agents/repo",
                "version": "abc123"
              }
            }
            """);
        var artifactBytes = new byte[96 * 1024];
        for (var i = 0; i < artifactBytes.Length; i++)
            artifactBytes[i] = (byte)(255 - i % 251);

        var mockArtifactStorage = CreateMockArtifactStorage(
            manifestBytes, "application/json", artifactBytes);

        await using var app = await CreateSubject(postconfigure: builder =>
        {
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockArtifactStorage));
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockWorker));
        });

        using var scope = app.Services.CreateScope();
        var processService = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var deploymentService = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        var projectId = Guid.NewGuid();
        var created = await processService.CreateAsync(
            projectId, EnvironmentId, "11.1.0", YamlFixture.ValidSimple, ct: cancellationToken);

        var result = await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        result.Status.Should().Be("Completed");

        await mockArtifactStorage.Received(1).DownloadArtifactAsync(
            "agents/repo", "abc123", Arg.Any<CancellationToken>());

        var firstCall = recorders[0].Written;
        var chunks = firstCall.Skip(1)
            .Select(x => x.ArtifactChunk)
            .ToList();
        var reassembled = chunks.SelectMany(c => c.Data.ToByteArray()).ToArray();
        reassembled.Should().Equal(artifactBytes);
        reassembled.Should().NotEqual(manifestBytes);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSnapshotIsBinaryArchive_StreamsSnapshotBytesToWorker()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (mockWorker, recorders) = CreateMockWorker(SuccessMessages("ok"));

        var snapshotPayload = new byte[128 * 1024];
        for (var i = 0; i < snapshotPayload.Length; i++)
            snapshotPayload[i] = (byte)(i % 199);

        var mockArtifactStorage = CreateMockArtifactStorage(snapshotPayload, "application/gzip");

        await using var app = await CreateSubject(postconfigure: builder =>
        {
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockArtifactStorage));
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockWorker));
        });

        using var scope = app.Services.CreateScope();
        var processService = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var deploymentService = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        var projectId = Guid.NewGuid();
        var created = await processService.CreateAsync(
            projectId, EnvironmentId, "11.2.0", YamlFixture.ValidSimple, ct: cancellationToken);

        var result = await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        result.Status.Should().Be("Completed");
        await mockArtifactStorage.DidNotReceive().DownloadArtifactAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        var chunks = recorders[0].Written.Skip(1)
            .Select(x => x.ArtifactChunk)
            .ToList();
        var reassembled = chunks.SelectMany(c => c.Data.ToByteArray()).ToArray();
        reassembled.Should().Equal(snapshotPayload);
    }

    [Fact]
    public async Task ExecuteAsync_WhenManifestHasNoSourceArtifact_FailsBeforeWorkerCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (mockWorker, recorders) = CreateMockWorker(SuccessMessages("should not run"));

        var invalidManifest = Encoding.UTF8.GetBytes("""{ "projectId": "proj-1" }""");
        var mockArtifactStorage = CreateMockArtifactStorage(invalidManifest, "application/json");

        await using var app = await CreateSubject(postconfigure: builder =>
        {
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockArtifactStorage));
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockWorker));
        });

        using var scope = app.Services.CreateScope();
        var processService = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var deploymentService = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        var projectId = Guid.NewGuid();
        var created = await processService.CreateAsync(
            projectId, EnvironmentId, "11.3.0", YamlFixture.ValidSimple, ct: cancellationToken);

        var result = await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        result.Status.Should().Be("Failed");
        result.ErrorType.Should().Be("SnapshotResolutionFailed");
        result.ErrorMessage.Should().Contain("sourceArtifact.key");
        result.ErrorMessage.Should().Contain("sourceArtifact.version");
        recorders.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenSourceArtifactNotFound_FailsBeforeWorkerCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (mockWorker, recorders) = CreateMockWorker(SuccessMessages("should not run"));

        var manifestBytes = Encoding.UTF8.GetBytes("""
            {
              "sourceArtifact": {
                "key": "agents/missing",
                "version": "missing-version"
              }
            }
            """);
        var mockArtifactStorage = CreateMockArtifactStorage(manifestBytes, "application/json");
        mockArtifactStorage.DownloadArtifactAsync(
                "agents/missing", "missing-version", Arg.Any<CancellationToken>())
            .Returns(new SnapshotResult.NotFound("agents/missing", "missing-version", "not found"));

        await using var app = await CreateSubject(postconfigure: builder =>
        {
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockArtifactStorage));
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockWorker));
        });

        using var scope = app.Services.CreateScope();
        var processService = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var deploymentService = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        var projectId = Guid.NewGuid();
        var created = await processService.CreateAsync(
            projectId, EnvironmentId, "11.4.0", YamlFixture.ValidSimple, ct: cancellationToken);

        var result = await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        result.Status.Should().Be("Failed");
        result.ErrorType.Should().Be("SnapshotResolutionFailed");
        result.ErrorMessage.Should().Contain("Source artifact 'agents/missing@missing-version'");
        recorders.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_InlineScriptStep_MapsToInlineScriptOneof()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (mockWorker, recorders) = CreateMockWorker(SuccessMessages("ok"));

        await using var app = await CreateSubject(postconfigure: builder =>
        {
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockWorker));
        });

        using var scope = app.Services.CreateScope();
        var processService = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var deploymentService = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        var projectId = Guid.NewGuid();
        var created = await processService.CreateAsync(
            projectId, EnvironmentId, "12.0.0", YamlFixture.ValidSimple, ct: cancellationToken);

        await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        var metadata = recorders[0].Written[0].Metadata;
        metadata.Command.SourceCase.Should().Be(StepCommand.SourceOneofCase.InlineScript);
        metadata.Command.InlineScript.Should().Be("dotnet build");
        metadata.Command.Type.Should().Be(CommandType.Shell);
    }

    [Fact]
    public async Task ExecuteAsync_ScriptPathStep_MapsToScriptPathOneof()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (mockWorker, recorders) = CreateMockWorker(SuccessMessages("ok"));

        await using var app = await CreateSubject(postconfigure: builder =>
        {
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockWorker));
        });

        using var scope = app.Services.CreateScope();
        var processService = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var deploymentService = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        var projectId = Guid.NewGuid();
        var created = await processService.CreateAsync(
            projectId, EnvironmentId, "13.0.0", YamlFixture.ValidWithScriptPath, ct: cancellationToken);

        await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        var metadata = recorders[0].Written[0].Metadata;
        metadata.Command.SourceCase.Should().Be(StepCommand.SourceOneofCase.ScriptPath);
        metadata.Command.ScriptPath.Should().Be("./scripts/build.sh");
        metadata.Command.Type.Should().Be(CommandType.Shell);
    }

    [Fact]
    public async Task ExecuteAsync_BuildsMetadataFromProcessStep_WithoutTargetType()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (mockWorker, recorders) = CreateMockWorker(SuccessMessages("ok"));

        await using var app = await CreateSubject(postconfigure: builder =>
        {
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockWorker));
        });

        using var scope = app.Services.CreateScope();
        var processService = scope.ServiceProvider.GetRequiredService<ProcessService>();
        var deploymentService = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        var projectId = Guid.NewGuid();
        var created = await processService.CreateAsync(
            projectId, EnvironmentId, "14.0.0", YamlFixture.ValidSimple, ct: cancellationToken);

        var result = await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        var metadata = recorders[0].Written[0].Metadata;

        metadata.DeploymentId.Should().Be(result.DeploymentId.ToString());
        metadata.ExecutionId.Should().NotBeNullOrWhiteSpace();
        metadata.ProcessId.Should().Be(created.Id.ToString());
        metadata.ProjectId.Should().Be(projectId.ToString());
        metadata.EnvironmentId.Should().Be(EnvironmentId.ToString());
        metadata.TargetId.Should().Be(TargetId);
        metadata.StepName.Should().Be("build");
        metadata.StepOrder.Should().Be(0);
        metadata.StepId.Should().NotBeNullOrWhiteSpace();
        metadata.Command.WorkingDirectory.Should().BeEmpty();
        metadata.Command.TimeoutSeconds.Should().Be(0);

        var descriptor = StepExecutionMetadata.Descriptor;
        descriptor.FindFieldByName("target_type").Should().BeNull();
    }

    private Task<Microsoft.AspNetCore.Builder.WebApplication> CreateSubject(
        ConfigureServices? preconfigure = null,
        ConfigureServices? postconfigure = null)
        => appFixture.BuildApplicationAsync(
            preconfigure: preconfigure,
            postconfigure: builder =>
            {
                var mockArtifactStorage = CreateMockArtifactStorage(DefaultSnapshotBytes);
                builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockArtifactStorage));
                postconfigure?.Invoke(builder);
            });

    private static ArtifactStorageGrpcService CreateMockArtifactStorage(
        byte[] snapshotBytes,
        string snapshotContentType = "application/gzip",
        byte[]? artifactBytes = null)
    {
        var mock = Substitute.For<ArtifactStorageGrpcService>(
            Substitute.For<ArtifactStorageService.ArtifactStorageServiceClient>());
        mock.DownloadSnapshotAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new SnapshotResult.Ok(
                callInfo.ArgAt<string>(0),
                callInfo.ArgAt<string>(1),
                snapshotBytes,
                snapshotContentType,
                snapshotBytes.LongLength,
                "abc123",
                DateTimeOffset.UtcNow));

        if (artifactBytes is not null)
        {
            mock.DownloadArtifactAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(callInfo => new SnapshotResult.Ok(
                    callInfo.ArgAt<string>(0),
                    callInfo.ArgAt<string>(1),
                    artifactBytes,
                    "application/gzip",
                    artifactBytes.LongLength,
                    "artifact-sha256",
                    DateTimeOffset.UtcNow));
        }

        return mock;
    }

    /// <summary>
    /// Creates a mocked <see cref="WorkerGrpcService"/> that returns a fresh duplex
    /// call per invocation. Each call gets its own response set; once all sets are
    /// consumed the last one is replayed indefinitely (mirrors the old sequenced
    /// helper used for multi-step pipelines).
    /// </summary>
    private static (WorkerGrpcService Worker, List<RecordingClientStreamWriter<StepExecutionInput>> Recorders)
        CreateMockWorker(params List<StepExecutionMessage>[] responsesPerCall)
    {
        if (responsesPerCall.Length == 0)
            throw new ArgumentException("At least one response set is required.", nameof(responsesPerCall));

        var mockWorker = Substitute.For<WorkerGrpcService>(
            Substitute.For<WorkerService.WorkerServiceClient>());
        var recorders = new List<RecordingClientStreamWriter<StepExecutionInput>>();

        var callIndex = 0;
        mockWorker
            .ExecuteStep(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var index = Math.Min(callIndex, responsesPerCall.Length - 1);
                callIndex++;
                var (call, recorder) = FakeDuplexStreamingCall
                    .Build<StepExecutionInput, StepExecutionMessage>(responsesPerCall[index]);
                recorders.Add(recorder);
                return call;
            });

        return (mockWorker, recorders);
    }

    private static List<StepExecutionMessage> SuccessMessages(string summary) =>
        [CreateSuccessResultMessage(summary)];

    private static List<StepExecutionMessage> ErrorMessages(string message, ErrorType type) =>
        [CreateErrorResultMessage(message, type)];

    private static StepExecutionMessage CreateLogMessage(string message, LogLevel level)
    {
        return new StepExecutionMessage
        {
            Log = new LogChunk
            {
                DeploymentId = Guid.NewGuid().ToString(),
                StepId       = Guid.NewGuid().ToString(),
                Timestamp    = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Level        = level,
                Message      = message,
                Source       = "worker",
            },
        };
    }

    private static StepExecutionMessage CreateSuccessResultMessage(string summary)
    {
        return new StepExecutionMessage
        {
            Result = new StepExecutionResult
            {
                Success = new StepExecutionResult.Types.Success
                {
                    DeploymentId = Guid.NewGuid().ToString(),
                    StepId       = Guid.NewGuid().ToString(),
                    CompletedAt  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Summary      = summary,
                },
            },
        };
    }

    private static StepExecutionMessage CreateErrorResultMessage(
        string errorMessage, ErrorType errorType)
    {
        return new StepExecutionMessage
        {
            Result = new StepExecutionResult
            {
                Error = new StepExecutionResult.Types.Error
                {
                    DeploymentId = Guid.NewGuid().ToString(),
                    StepId       = Guid.NewGuid().ToString(),
                    ErrorType    = errorType,
                    Message      = errorMessage,
                },
            },
        };
    }
}
