using System.Threading;
using Headless.EntityFramework;
using Headless.EntityFramework.Messaging;
using Microsoft.EntityFrameworkCore.Storage;

namespace Tests.Fixture;

public enum DispatchKind
{
    Local = 0,
    Distributed = 1,
}

public sealed record DispatchCall(int Index, DispatchKind Kind, object Payload);

public sealed class RecordingHeadlessMessageDispatcher : IHeadlessMessageDispatcher
{
    private int _callIndex;
    private readonly List<DispatchCall> _calls = [];

    public List<EmitterDistributedMessages> EmittedDistributedMessages { get; } = [];

    public List<EmitterLocalMessages> EmittedLocalMessages { get; } = [];

    public IReadOnlyList<DispatchCall> Calls => _calls;

    public int NextIndex() => Interlocked.Increment(ref _callIndex);

    public void RecordExternal(DispatchKind kind, object payload)
    {
        _calls.Add(new DispatchCall(NextIndex(), kind, payload));
    }

    public Task PublishLocalAsync(
        IReadOnlyList<EmitterLocalMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    )
    {
        EmittedLocalMessages.AddRange(emitters);
        _calls.Add(new DispatchCall(NextIndex(), DispatchKind.Local, emitters));
        return Task.CompletedTask;
    }

    public void PublishLocal(IReadOnlyList<EmitterLocalMessages> emitters, IDbContextTransaction currentTransaction)
    {
        EmittedLocalMessages.AddRange(emitters);
        _calls.Add(new DispatchCall(NextIndex(), DispatchKind.Local, emitters));
    }

    public Task EnqueueDistributedAsync(
        IReadOnlyList<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    )
    {
        EmittedDistributedMessages.AddRange(emitters);
        _calls.Add(new DispatchCall(NextIndex(), DispatchKind.Distributed, emitters));
        return Task.CompletedTask;
    }

    public void EnqueueDistributed(
        IReadOnlyList<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction
    )
    {
        EmittedDistributedMessages.AddRange(emitters);
        _calls.Add(new DispatchCall(NextIndex(), DispatchKind.Distributed, emitters));
    }
}
