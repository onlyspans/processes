using Grpc.Core;

namespace Onlyspans.Processes.Api.Tests.Fixtures;

public sealed class FakeAsyncStreamReader<T> : IAsyncStreamReader<T>
{
    private readonly Queue<T> _messages;

    public FakeAsyncStreamReader(IEnumerable<T> messages)
    {
        _messages = new Queue<T>(messages);
    }

    public T Current { get; private set; } = default!;

    public Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        if (_messages.Count > 0)
        {
            Current = _messages.Dequeue();
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }
}

public static class FakeGrpcStreamHelper
{
    public static AsyncServerStreamingCall<T> CreateServerStreamingCall<T>(
        IEnumerable<T> messages)
    {
        var reader = new FakeAsyncStreamReader<T>(messages);
        return new AsyncServerStreamingCall<T>(
            reader,
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
    }
}
