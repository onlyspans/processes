using Grpc.Core;

namespace Onlyspans.Processes.Api.Tests.Fixtures;

/// <summary>
/// Captures every <see cref="IClientStreamWriter{T}.WriteAsync"/> call and the
/// completion signal so tests can assert on the full request stream that the
/// SUT sent into the duplex Worker call.
/// </summary>
public sealed class RecordingClientStreamWriter<T> : IClientStreamWriter<T>
{
    private readonly List<T> _written = [];

    public IReadOnlyList<T> Written => _written;
    public bool Completed { get; private set; }

    public WriteOptions? WriteOptions { get; set; }

    public Task WriteAsync(T message)
    {
        _written.Add(message);
        return Task.CompletedTask;
    }

    public Task CompleteAsync()
    {
        Completed = true;
        return Task.CompletedTask;
    }
}

public static class FakeDuplexStreamingCall
{
    public static (AsyncDuplexStreamingCall<TRequest, TResponse> Call,
                   RecordingClientStreamWriter<TRequest> Recorder)
        Build<TRequest, TResponse>(IEnumerable<TResponse> responses)
    {
        var recorder = new RecordingClientStreamWriter<TRequest>();
        var reader = new FakeAsyncStreamReader<TResponse>(responses);

        var call = new AsyncDuplexStreamingCall<TRequest, TResponse>(
            recorder,
            reader,
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

        return (call, recorder);
    }
}
