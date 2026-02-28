using ArtifactStorage.Communication;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
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

    [Fact]
    public async Task ExecuteAsync_AllStepsSucceed_ProcessMarkedAsCompleted()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mockWorker = CreateMockWorkerWithSuccessResponse("All steps completed");

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

        // Act
        var result = await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        // Assert
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
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mockWorker = CreateMockWorkerWithSuccessResponse("Done");

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

        // Act
        await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        // Assert
        var process = await processService.GetByIdAsync(created.Id, ct: cancellationToken);
        process.Should().NotBeNull();
        process!.Steps.Should().OnlyContain(s => s.Status == "Succeeded");
    }

    [Fact]
    public async Task ExecuteAsync_StepFails_ProcessMarkedAsFailed()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mockWorker = CreateMockWorkerWithErrorResponse(
            "Build failed", ErrorType.TargetExecutionFailed);

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

        // Act
        var result = await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        // Assert
        result.Status.Should().Be("Failed");
        result.ErrorMessage.Should().Be("Build failed");
        result.ErrorType.Should().Contain("TargetExecutionFailed");
    }

    [Fact]
    public async Task ExecuteAsync_WorkerStreamsLogs_LogsWrittenToFile()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var logMessages = new List<DeploymentMessage>
        {
            CreateLogMessage("Starting build...", LogLevel.Info),
            CreateLogMessage("Compiling source...", LogLevel.Info),
            CreateLogMessage("Build complete", LogLevel.Info),
            CreateSuccessResultMessage("Build succeeded"),
        };

        var mockWorker = Substitute.For<WorkerGrpcService>(
            Substitute.For<WorkerService.WorkerServiceClient>());
        mockWorker
            .ExecuteDeployment(Arg.Any<DeploymentPackage>(), Arg.Any<CancellationToken>())
            .Returns(_ => FakeGrpcStreamHelper.CreateServerStreamingCall(logMessages));

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

        // Act
        var result = await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        var logs = await deploymentService.GetLogsAsync(result.DeploymentId, ct: cancellationToken);

        // Assert
        logs.DeploymentId.Should().Be(result.DeploymentId);
        logs.Entries.Should().HaveCountGreaterOrEqualTo(3);
        logs.Entries.Should().Contain(e => e.Message == "Starting build...");
        logs.Entries.Should().Contain(e => e.Message == "Compiling source...");
        logs.Entries.Should().Contain(e => e.Message == "Build complete");
    }

    [Fact]
    public async Task ExecuteAsync_ApprovalStep_ReturnsAwaitingApproval()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mockWorker = CreateMockWorkerWithSuccessResponse("Done");

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

        // Act
        var result = await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        // Assert
        result.Status.Should().Be("AwaitingApproval");
    }

    [Fact]
    public async Task ExecuteAsync_NonExistentProcess_ThrowsException()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mockWorker = CreateMockWorkerWithSuccessResponse("Done");

        await using var app = await CreateSubject(postconfigure: builder =>
        {
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockWorker));
        });

        using var scope = app.Services.CreateScope();
        var deploymentService = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        // Act
        var act = async () => await deploymentService.ExecuteAsync(
            Guid.NewGuid(), TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task ExecuteAsync_WorkerThrowsException_ProcessMarkedAsFailed()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mockWorker = Substitute.For<WorkerGrpcService>(
            Substitute.For<WorkerService.WorkerServiceClient>());
        mockWorker
            .ExecuteDeployment(Arg.Any<DeploymentPackage>(), Arg.Any<CancellationToken>())
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

        // Act
        var result = await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        // Assert
        result.Status.Should().Be("Failed");
        result.ErrorMessage.Should().Contain("Worker unreachable");
    }

    [Fact]
    public async Task ExecuteAsync_WorkerThrowsException_ErrorLogWritten()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mockWorker = Substitute.For<WorkerGrpcService>(
            Substitute.For<WorkerService.WorkerServiceClient>());
        mockWorker
            .ExecuteDeployment(Arg.Any<DeploymentPackage>(), Arg.Any<CancellationToken>())
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

        // Act
        var result = await deploymentService.ExecuteAsync(
            created.Id, TargetId, TargetType, SnapshotKey, ct: cancellationToken);

        var logs = await deploymentService.GetLogsAsync(result.DeploymentId, ct: cancellationToken);

        // Assert
        logs.Entries.Should().Contain(e =>
            e.Level == "ERROR" && e.Source == "processes");
    }

    [Fact]
    public async Task GetLogsAsync_NoLogs_ReturnsEmptyEntries()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mockWorker = CreateMockWorkerWithSuccessResponse("Done");

        await using var app = await CreateSubject(postconfigure: builder =>
        {
            builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockWorker));
        });

        using var scope = app.Services.CreateScope();
        var deploymentService = scope.ServiceProvider.GetRequiredService<DeploymentService>();

        // Act
        var logs = await deploymentService.GetLogsAsync(Guid.NewGuid(), ct: cancellationToken);

        // Assert
        logs.Entries.Should().BeEmpty();
    }

    private Task<Microsoft.AspNetCore.Builder.WebApplication> CreateSubject(
        ConfigureServices? preconfigure = null,
        ConfigureServices? postconfigure = null)
        => appFixture.BuildApplicationAsync(
            preconfigure: preconfigure,
            postconfigure: builder =>
            {
                var mockArtifactStorage = CreateMockArtifactStorage();
                builder.Services.Replace(ServiceDescriptor.Scoped(_ => mockArtifactStorage));
                postconfigure?.Invoke(builder);
            });

    private static ArtifactStorageGrpcService CreateMockArtifactStorage()
    {
        var mock = Substitute.For<ArtifactStorageGrpcService>(
            Substitute.For<ArtifactStorageService.ArtifactStorageServiceClient>());
        mock.GetSnapshotAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new SnapshotResult.Ok(
                callInfo.ArgAt<string>(0),
                new byte[] { 0x01, 0x02 },
                "application/gzip",
                2,
                DateTimeOffset.UtcNow));
        return mock;
    }

    private static WorkerGrpcService CreateMockWorkerWithSuccessResponse(string summary)
    {
        var messages = new List<DeploymentMessage>
        {
            CreateSuccessResultMessage(summary),
        };

        var mockWorker = Substitute.For<WorkerGrpcService>(
            Substitute.For<WorkerService.WorkerServiceClient>());
        mockWorker
            .ExecuteDeployment(Arg.Any<DeploymentPackage>(), Arg.Any<CancellationToken>())
            .Returns(_ => FakeGrpcStreamHelper.CreateServerStreamingCall(messages));

        return mockWorker;
    }

    private static WorkerGrpcService CreateMockWorkerWithErrorResponse(
        string errorMessage, ErrorType errorType)
    {
        var resultMessage = new DeploymentMessage
        {
            Result = new DeploymentResult
            {
                Error = new DeploymentResult.Types.Error
                {
                    DeploymentId = Guid.NewGuid().ToString(),
                    ErrorType    = errorType,
                    Message      = errorMessage,
                },
            },
        };

        var mockWorker = Substitute.For<WorkerGrpcService>(
            Substitute.For<WorkerService.WorkerServiceClient>());
        mockWorker
            .ExecuteDeployment(Arg.Any<DeploymentPackage>(), Arg.Any<CancellationToken>())
            .Returns(_ => FakeGrpcStreamHelper.CreateServerStreamingCall([resultMessage]));

        return mockWorker;
    }

    private static DeploymentMessage CreateLogMessage(string message, LogLevel level)
    {
        return new DeploymentMessage
        {
            Log = new LogChunk
            {
                DeploymentId = Guid.NewGuid().ToString(),
                Timestamp    = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Level        = level,
                Message      = message,
                Source       = "worker",
            },
        };
    }

    private static DeploymentMessage CreateSuccessResultMessage(string summary)
    {
        return new DeploymentMessage
        {
            Result = new DeploymentResult
            {
                Success = new DeploymentResult.Types.Success
                {
                    DeploymentId = Guid.NewGuid().ToString(),
                    CompletedAt  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Summary      = summary,
                },
            },
        };
    }
}
