using FluentAssertions;
using Onlyspans.Processes.Api.Features.Deployment;

namespace Onlyspans.Processes.Api.Tests.Unit.Deployment;

public sealed class FileDeploymentLogWriterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileDeploymentLogWriter _writer;

    public FileDeploymentLogWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "log-writer-tests", Guid.NewGuid().ToString());
        _writer = new FileDeploymentLogWriter(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task AppendAsync_SingleEntry_CanBeReadBack()
    {
        // Arrange
        var deploymentId = Guid.NewGuid();
        var entry = new DeploymentLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Level     = "LOG_LEVEL_INFO",
            Message   = "Starting deployment",
            Source    = "worker",
        };

        // Act
        await _writer.AppendAsync(deploymentId, entry);
        var entries = await _writer.ReadAsync(deploymentId);

        // Assert
        entries.Should().HaveCount(1);
        entries[0].Message.Should().Be("Starting deployment");
        entries[0].Level.Should().Be("LOG_LEVEL_INFO");
        entries[0].Source.Should().Be("worker");
    }

    [Fact]
    public async Task AppendAsync_MultipleEntries_PreservesOrder()
    {
        // Arrange
        var deploymentId = Guid.NewGuid();
        var entries = Enumerable.Range(1, 5).Select(i => new DeploymentLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow.AddSeconds(i),
            Level     = "LOG_LEVEL_INFO",
            Message   = $"Step {i}",
        }).ToList();

        // Act
        foreach (var entry in entries)
            await _writer.AppendAsync(deploymentId, entry);

        var result = await _writer.ReadAsync(deploymentId);

        // Assert
        result.Should().HaveCount(5);
        result[0].Message.Should().Be("Step 1");
        result[4].Message.Should().Be("Step 5");
    }

    [Fact]
    public async Task ReadAsync_NoLogFile_ReturnsEmptyList()
    {
        // Arrange
        var deploymentId = Guid.NewGuid();

        // Act
        var entries = await _writer.ReadAsync(deploymentId);

        // Assert
        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadFromOffsetAsync_ReadsOnlyNewEntries()
    {
        // Arrange
        var deploymentId = Guid.NewGuid();
        await _writer.AppendAsync(deploymentId, new DeploymentLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Level     = "LOG_LEVEL_INFO",
            Message   = "First entry",
        });

        var logFilePath = Path.Combine(_tempDir, $"{deploymentId}.jsonl");
        var offsetAfterFirst = new FileInfo(logFilePath).Length;

        await _writer.AppendAsync(deploymentId, new DeploymentLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Level     = "LOG_LEVEL_INFO",
            Message   = "Second entry",
        });

        // Act
        var entries = await _writer.ReadFromOffsetAsync(deploymentId, offsetAfterFirst);

        // Assert
        entries.Should().HaveCount(1);
        entries[0].Message.Should().Be("Second entry");
    }

    [Fact]
    public async Task ReadFromOffsetAsync_OffsetBeyondFile_ReturnsEmpty()
    {
        // Arrange
        var deploymentId = Guid.NewGuid();
        await _writer.AppendAsync(deploymentId, new DeploymentLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Level     = "LOG_LEVEL_INFO",
            Message   = "Only entry",
        });

        // Act
        var entries = await _writer.ReadFromOffsetAsync(deploymentId, 999999);

        // Assert
        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadFromOffsetAsync_NoFile_ReturnsEmpty()
    {
        // Arrange
        var deploymentId = Guid.NewGuid();

        // Act
        var entries = await _writer.ReadFromOffsetAsync(deploymentId, 0);

        // Assert
        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task AppendAsync_NullSource_DeserializesCorrectly()
    {
        // Arrange
        var deploymentId = Guid.NewGuid();
        var entry = new DeploymentLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Level     = "LOG_LEVEL_WARNING",
            Message   = "Warning without source",
            Source    = null,
        };

        // Act
        await _writer.AppendAsync(deploymentId, entry);
        var entries = await _writer.ReadAsync(deploymentId);

        // Assert
        entries.Should().HaveCount(1);
        entries[0].Source.Should().BeNull();
        entries[0].Level.Should().Be("LOG_LEVEL_WARNING");
    }

    [Fact]
    public async Task AppendAsync_DifferentDeployments_IsolatedLogFiles()
    {
        // Arrange
        var deployment1 = Guid.NewGuid();
        var deployment2 = Guid.NewGuid();

        // Act
        await _writer.AppendAsync(deployment1, new DeploymentLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Level     = "LOG_LEVEL_INFO",
            Message   = "Deployment 1 log",
        });

        await _writer.AppendAsync(deployment2, new DeploymentLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Level     = "LOG_LEVEL_INFO",
            Message   = "Deployment 2 log",
        });

        var entries1 = await _writer.ReadAsync(deployment1);
        var entries2 = await _writer.ReadAsync(deployment2);

        // Assert
        entries1.Should().HaveCount(1);
        entries1[0].Message.Should().Be("Deployment 1 log");

        entries2.Should().HaveCount(1);
        entries2[0].Message.Should().Be("Deployment 2 log");
    }
}
