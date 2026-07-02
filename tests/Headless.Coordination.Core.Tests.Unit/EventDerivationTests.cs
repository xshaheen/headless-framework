// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class EventDerivationTests : TestBase
{
    private readonly List<object> _disposables = [];

    [Fact]
    public async Task should_emit_joined_once_for_new_alive_identity()
    {
        // given
        var node = _Identity("node-a", 1);
        var store = new FakeMembershipStore();
        store.EnqueueSnapshot(_Snapshot(node, NodeLivenessState.Alive));
        store.EnqueueSnapshot(_Snapshot(node, NodeLivenessState.Alive));
        var source = new MembershipEventSource(NullLogger<MembershipEventSource>.Instance);
        var sut = _CreateHeartbeatService(store, source);
        using var watcherCts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        await using var watcher = source.WatchAsync(watcherCts.Token).GetAsyncEnumerator(watcherCts.Token);

        // when
        await sut.RunOnceAsync(AbortToken);
        var first = await _ReadNextAsync(watcher);
        await sut.RunOnceAsync(AbortToken);

        // then
        first.Should().BeOfType<NodeJoined>().Which.Identity.Should().Be(node);
        await _ShouldHaveNoImmediateEventAsync(watcher, watcherCts);
    }

    [Fact]
    public async Task should_emit_suspected_recovered_and_left_transitions()
    {
        // given
        var node = _Identity("node-a", 1);
        var store = new FakeMembershipStore();
        store.EnqueueSnapshot(_Snapshot(node, NodeLivenessState.Alive));
        store.EnqueueSnapshot(_Snapshot(node, NodeLivenessState.Suspected));
        store.EnqueueSnapshot(_Snapshot(node, NodeLivenessState.Alive));
        store.EnqueueSnapshot(_Snapshot(node, NodeLivenessState.Dead));
        var source = new MembershipEventSource(NullLogger<MembershipEventSource>.Instance);
        var sut = _CreateHeartbeatService(store, source);
        using var watcherCts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        await using var watcher = source.WatchAsync(watcherCts.Token).GetAsyncEnumerator(watcherCts.Token);

        // when
        await sut.RunOnceAsync(AbortToken);
        var joined = await _ReadNextAsync(watcher);
        await sut.RunOnceAsync(AbortToken);
        var suspected = await _ReadNextAsync(watcher);
        await sut.RunOnceAsync(AbortToken);
        var recovered = await _ReadNextAsync(watcher);
        await sut.RunOnceAsync(AbortToken);
        var left = await _ReadNextAsync(watcher);

        // then
        joined.Should().BeOfType<NodeJoined>().Which.Identity.Should().Be(node);
        suspected.Should().BeOfType<NodeSuspected>().Which.Identity.Should().Be(node);
        recovered.Should().BeOfType<NodeRecovered>().Which.Identity.Should().Be(node);
        left.Should().BeOfType<NodeLeft>().Which.Identity.Should().Be(node);
    }

    [Fact]
    public async Task should_emit_left_for_superseded_identity_and_joined_for_new_incarnation()
    {
        // given
        var oldIdentity = _Identity("node-a", 1);
        var newIdentity = _Identity("node-a", 2);
        var store = new FakeMembershipStore();
        store.EnqueueSnapshot(_Snapshot(oldIdentity, NodeLivenessState.Alive));
        store.EnqueueSnapshot(_Snapshot(newIdentity, NodeLivenessState.Alive));
        var source = new MembershipEventSource(NullLogger<MembershipEventSource>.Instance);
        var sut = _CreateHeartbeatService(store, source);
        await using var watcher = source.WatchAsync(AbortToken).GetAsyncEnumerator(AbortToken);

        // when
        await sut.RunOnceAsync(AbortToken);
        _ = await _ReadNextAsync(watcher);
        await sut.RunOnceAsync(AbortToken);
        var first = await _ReadNextAsync(watcher);
        var second = await _ReadNextAsync(watcher);

        // then
        var events = new[] { first, second };

        events.Where(@event => @event is NodeLeft && @event.Identity == oldIdentity).Should().ContainSingle();
        events.Where(@event => @event is NodeJoined && @event.Identity == newIdentity).Should().ContainSingle();
    }

    [Fact]
    public async Task should_not_emit_stale_left_when_liveness_read_fails()
    {
        // given
        var node = _Identity("node-a", 1);
        var store = new FakeMembershipStore();
        store.EnqueueSnapshot(_Snapshot(node, NodeLivenessState.Alive));
        var source = new MembershipEventSource(NullLogger<MembershipEventSource>.Instance);
        var sut = _CreateHeartbeatService(store, source);
        using var watcherCts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        await using var watcher = source.WatchAsync(watcherCts.Token).GetAsyncEnumerator(watcherCts.Token);
        await sut.RunOnceAsync(AbortToken);
        _ = await _ReadNextAsync(watcher);
        store.ThrowOnRead = true;

        // when
        await sut.RunOnceAsync(AbortToken);

        // then
        await _ShouldHaveNoImmediateEventAsync(watcher, watcherCts);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var disposable in _disposables)
        {
            switch (disposable)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable syncDisposable:
                    syncDisposable.Dispose();
                    break;
            }
        }

        await base.DisposeAsyncCore().ConfigureAwait(false);
    }

    private MembershipHeartbeatBackgroundService _CreateHeartbeatService(
        FakeMembershipStore store,
        MembershipEventSource source
    )
    {
        var service = new MembershipService(
            store,
            new StaticNodeIdProvider(_Identity("local", 1).NodeId),
            new CoordinationOptions(),
            source,
            new FakeHostApplicationLifetime(),
            NullLogger<MembershipService>.Instance
        );
        _disposables.Add(service);

        var heartbeat = new MembershipHeartbeatBackgroundService(
            service,
            source,
            new CoordinationOptions(),
            new FakeTimeProvider(),
            NullLogger<MembershipHeartbeatBackgroundService>.Instance
        );
        _disposables.Add(heartbeat);

        return heartbeat;
    }

    private static NodeIdentity _Identity(string nodeId, long incarnation)
    {
        return new NodeIdentity(new NodeId(nodeId), new NodeIncarnation(incarnation));
    }

    private static NodeLivenessSnapshot _Snapshot(NodeIdentity identity, NodeLivenessState state)
    {
        return new NodeLivenessSnapshot(identity, state, null, new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private static async Task<NodeMembershipEvent> _ReadNextAsync(IAsyncEnumerator<NodeMembershipEvent> watcher)
    {
        (await watcher.MoveNextAsync()).Should().BeTrue();

        return watcher.Current;
    }

    private static async Task _ShouldHaveNoImmediateEventAsync(
        IAsyncEnumerator<NodeMembershipEvent> watcher,
        CancellationTokenSource watcherCts
    )
    {
        // Publish is synchronous, so by now any emitted event is already buffered. If one were buffered,
        // MoveNextAsync would complete with the value before cancellation; otherwise it stays pending and the
        // cancellation surfaces deterministically as OperationCanceledException (no wall-clock delay needed).
        var moveNext = watcher.MoveNextAsync();

        await watcherCts.CancelAsync();
        var act = async () => await moveNext;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class StaticNodeIdProvider(NodeId nodeId) : INodeIdProvider
    {
        public ValueTask<NodeId> GetNodeIdAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult(nodeId);
        }
    }
}
