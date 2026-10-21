// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Headless.Coordination;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tests.Guarantees;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

/// <summary>
/// Cross-provider end-to-end conformance for messaging dead-owner recovery. Hosts a real
/// <c>DeadOwnerRecoveryBridge&lt;MessagingDeadOwnerReclaimer&gt;</c> (resolved through the messaging DI graph, since
/// the bridge type is internal to Coordination) against a real provider store and a
/// <see cref="ControlledNodeMembership"/>, then asserts outbox/inbox row outcomes. One scenario replaces the controlled
/// membership with the provider's real coordination store, so a node is declared dead by the store's clock.
/// </summary>
/// <remarks>
/// Each provider supplies its store via <see cref="ConfigureStorage"/>; the scenarios are owned here so they are
/// not copy-pasted per provider (CLAUDE.md harness rule). Only the bridge hosted service is started — the
/// Bootstrapper and processing servers are left unstarted so no transport is required.
/// </remarks>
[PublicAPI]
public abstract class DeadOwnerReclaimConformanceTests : TestBase
{
    private static readonly TimeSpan _ReconcileInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan _PositiveTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _NegativeSettle = TimeSpan.FromMilliseconds(750);
    private static long _messageCounter;

    private readonly List<IHostedService> _startedBridges = [];
    private readonly List<ServiceProvider> _providers = [];

    /// <summary>The membership the primary bridge host runs under and that stamps the owner column when leasing.</summary>
    protected ControlledNodeMembership Membership { get; } = new();

    /// <summary>Registers the provider store on the messaging setup (e.g. <c>setup.UseInMemoryStorage()</c>).</summary>
    protected abstract void ConfigureStorage(MessagingSetupBuilder setup);

    /// <summary>Clears the store before a test seeds it (a no-op for per-instance InMemory; TRUNCATE for shared SQL DBs).</summary>
    protected virtual Task ResetStorageAsync(IDataStorage storage)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Whether a second concurrent bridge host shares the <em>same</em> <see cref="IDataStorage"/> instance
    /// (InMemory, whose state is per-instance) instead of a separate instance over the same database (SQL providers
    /// naturally share through the connection string).
    /// </summary>
    protected virtual bool SharesStorageInstanceForConcurrency => false;

    /// <summary>
    /// Selects the real coordination provider over the store's database (e.g. <c>setup.UsePostgreSql(...)</c>), or
    /// <see langword="null"/> for a store that has none, which skips the real-membership scenario.
    /// </summary>
    protected virtual Action<HeadlessCoordinationSetupBuilder>? CoordinationProvider => null;

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var bridge in _startedBridges)
        {
            try
            {
                await bridge.StopAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Best-effort teardown; a bridge that never fully started must not mask the test result.
                Logger.LogDebug(ex, "Ignoring bridge stop failure during conformance teardown");
            }
        }

        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }

        await base.DisposeAsyncCore();
    }

    [Guarantee("MSG-COORD-01")]
    public virtual async Task should_reclaim_dead_owner_published_and_received_rows()
    {
        // A Dead owner's in-flight rows are re-dispatched, not skipped.
        var (_, storage) = await _StartPrimaryStackAsync();
        var dead = await _SeedOwnedRowsAsync(storage, "dead-node", incarnation: 5, _FutureLease());
        var local = Membership.SetIdentity("local-node", incarnation: 1);
        Membership.SetSnapshot(
            ControlledNodeMembership.Snapshot(local, NodeLivenessState.Alive),
            ControlledNodeMembership.Snapshot(dead.Owner, NodeLivenessState.Dead)
        );

        await _StartBridgeAsync(_providers[0]);

        (await _BecomesRetriableAsync(storage, dead.Published, published: true))
            .Should()
            .BeTrue("the dead owner's published row must be reclaimed and re-dispatched");
        (await _BecomesRetriableAsync(storage, dead.Received, published: false))
            .Should()
            .BeTrue("the dead owner's received row must be reclaimed and re-dispatched");
    }

    [Guarantee("MSG-COORD-01")]
    public virtual async Task should_not_reclaim_suspected_owner_rows()
    {
        // A Suspected owner is never reclaimed through watch or reconcile.
        var (_, storage) = await _StartPrimaryStackAsync();
        var suspected = await _SeedOwnedRowsAsync(storage, "suspected-node", incarnation: 5, _FutureLease());
        var local = Membership.SetIdentity("local-node", incarnation: 1);
        Membership.SetSnapshot(
            ControlledNodeMembership.Snapshot(local, NodeLivenessState.Alive),
            ControlledNodeMembership.Snapshot(suspected.Owner, NodeLivenessState.Suspected)
        );

        await _StartBridgeAsync(_providers[0]);
        // A Suspected node also emits a NodeSuspected event — neither path may reclaim it.
        Membership.Emit(new NodeSuspected(suspected.Owner));
        await Task.Delay(_NegativeSettle, AbortToken);

        (await _IsRetriableAsync(storage, suspected.Published, published: true))
            .Should()
            .BeFalse("a suspected owner's published row stays leased");
        (await _IsRetriableAsync(storage, suspected.Received, published: false))
            .Should()
            .BeFalse("a suspected owner's received row stays leased");
    }

    [Guarantee("MSG-COORD-02")]
    public virtual async Task should_reclaim_once_when_surfaced_by_both_event_and_reconcile()
    {
        // A Dead owner surfaced by both a NodeLeft event and the reconcile is reclaimed (dedup, no thrash).
        var (_, storage) = await _StartPrimaryStackAsync();
        var dead = await _SeedOwnedRowsAsync(storage, "dead-node", incarnation: 9, _FutureLease());
        var local = Membership.SetIdentity("local-node", incarnation: 1);
        Membership.SetSnapshot(
            ControlledNodeMembership.Snapshot(local, NodeLivenessState.Alive),
            ControlledNodeMembership.Snapshot(dead.Owner, NodeLivenessState.Dead)
        );

        await _StartBridgeAsync(_providers[0]);
        Membership.EmitNodeLeft(dead.Owner);

        (await _BecomesRetriableAsync(storage, dead.Published, published: true)).Should().BeTrue();
        (await _BecomesRetriableAsync(storage, dead.Received, published: false)).Should().BeTrue();
    }

    [Guarantee("MSG-COORD-03")]
    public virtual async Task should_fence_a_restarted_incarnation()
    {
        // node@N dead, node@N+1 live (restart) → only the dead incarnation's rows are reclaimed.
        var (_, storage) = await _StartPrimaryStackAsync();
        var dead = await _SeedOwnedRowsAsync(storage, "restart-node", incarnation: 5, _FutureLease());
        var live = await _SeedOwnedRowsAsync(storage, "restart-node", incarnation: 6, _FutureLease());
        var local = Membership.SetIdentity("local-node", incarnation: 1);
        Membership.SetSnapshot(
            ControlledNodeMembership.Snapshot(local, NodeLivenessState.Alive),
            ControlledNodeMembership.Snapshot(dead.Owner, NodeLivenessState.Dead),
            ControlledNodeMembership.Snapshot(live.Owner, NodeLivenessState.Alive)
        );

        await _StartBridgeAsync(_providers[0]);

        (await _BecomesRetriableAsync(storage, dead.Published, published: true)).Should().BeTrue();
        (await _BecomesRetriableAsync(storage, dead.Received, published: false)).Should().BeTrue();
        (await _IsRetriableAsync(storage, live.Published, published: true))
            .Should()
            .BeFalse("the restarted incarnation's published row must not be reclaimed by the dead incarnation's state");
        (await _IsRetriableAsync(storage, live.Received, published: false))
            .Should()
            .BeFalse("the restarted incarnation's received row must not be reclaimed");
    }

    public virtual async Task should_recover_aged_out_owner_via_lease_floor()
    {
        // A dead owner that aged out of the snapshot before reclaim still recovers via LockedUntil expiry.
        var (_, storage) = await _StartPrimaryStackAsync();
        // Lease already expired and the owner is absent from the snapshot — the bridge cannot act, only the floor.
        var aged = await _SeedOwnedRowsAsync(storage, "aged-out-node", incarnation: 5, _ExpiredLease());
        var local = Membership.SetIdentity("local-node", incarnation: 1);
        Membership.SetSnapshot(ControlledNodeMembership.Snapshot(local, NodeLivenessState.Alive));

        await _StartBridgeAsync(_providers[0]);

        (await _BecomesRetriableAsync(storage, aged.Published, published: true))
            .Should()
            .BeTrue("an expired lease is recovered by the per-row floor even with no bridge reclaim");
        (await _BecomesRetriableAsync(storage, aged.Received, published: false))
            .Should()
            .BeTrue("an expired lease is recovered by the per-row floor even with no bridge reclaim");
    }

    [Guarantee("MSG-COORD-02")]
    public virtual async Task should_reclaim_once_under_two_concurrent_bridges()
    {
        // Concurrency: two bridge hosts reclaiming the same Dead owner → rows reclaimed, no duplicate-thrash error
        // (the owner-scoped conditional UPDATE is idempotent).
        var (_, storage) = await _StartPrimaryStackAsync();
        var dead = await _SeedOwnedRowsAsync(storage, "dead-node", incarnation: 5, _FutureLease());
        var local = Membership.SetIdentity("local-node", incarnation: 1);
        Membership.SetSnapshot(
            ControlledNodeMembership.Snapshot(local, NodeLivenessState.Alive),
            ControlledNodeMembership.Snapshot(dead.Owner, NodeLivenessState.Dead)
        );

        // Second host: shares the same store (same InMemory instance, or same SQL database via the connection string).
        var (secondProvider, _) = await _BuildStackAsync(SharesStorageInstanceForConcurrency ? storage : null);

        await _StartBridgeAsync(_providers[0]);
        await _StartBridgeAsync(secondProvider);

        (await _BecomesRetriableAsync(storage, dead.Published, published: true)).Should().BeTrue();
        (await _BecomesRetriableAsync(storage, dead.Received, published: false)).Should().BeTrue();
    }

    [Fact]
    [Guarantee("MSG-COORD-04")]
    public async Task should_reclaim_a_killed_nodes_rows_once_through_a_real_coordination_provider()
    {
        if (CoordinationProvider is not { } coordinationProvider)
        {
            Assert.Skip("This store has no real coordination provider.");
            return;
        }

        // given: two hosts of one cluster on the real membership store, and a doomed node that registered and leased
        // a published and a received row for an hour
        var cluster = $"reclaim-{Guid.NewGuid():N}";
        var (doomedProvider, doomedStorage) = await _BuildCoordinatedStackAsync(
            "doomed-node",
            cluster,
            coordinationProvider
        );
        var (survivorProvider, survivorStorage) = await _BuildCoordinatedStackAsync(
            "survivor-node",
            cluster,
            coordinationProvider
        );
        await ResetStorageAsync(doomedStorage);
        var doomedIdentity = await doomedProvider.GetRequiredService<INodeMembership>().RegisterAsync(AbortToken);
        var rows = await _SeedLeasedRowsAsync(doomedStorage, "doomed-node", _FutureLease());

        // when: the doomed node is killed, so it never beats again, and the survivor runs its dead-owner bridge
        await _StartBridgeAsync(survivorProvider);

        // then: once the store's clock declares the node dead, the survivor claims each of its rows, and only once
        (await _BecomesRetriableAsync(survivorStorage, rows.Published, published: true))
            .Should()
            .BeTrue("a killed node's published row must return to the retry processor long before its lease ends");
        (await _BecomesRetriableAsync(survivorStorage, rows.Received, published: false))
            .Should()
            .BeTrue("a killed node's received row must return to the retry processor long before its lease ends");
        (await _IsRetriableAsync(survivorStorage, rows.Published, published: true))
            .Should()
            .BeFalse("the survivor's claim leases the published row, so it is not handed out a second time");
        (await _IsRetriableAsync(survivorStorage, rows.Received, published: false))
            .Should()
            .BeFalse("the survivor's claim leases the received row, so it is not handed out a second time");
        (await survivorProvider.GetRequiredService<INodeMembership>().GetLivenessSnapshotAsync(AbortToken))
            .Should()
            .Contain(node => node.Identity == doomedIdentity && node.State == NodeLivenessState.Dead);
    }

    private async Task<(IServiceProvider Provider, IDataStorage Storage)> _StartPrimaryStackAsync()
    {
        var stack = await _BuildStackAsync(sharedStorage: null);
        await ResetStorageAsync(stack.Storage);
        return stack;
    }

    private async Task<(IServiceProvider Provider, IDataStorage Storage)> _BuildStackAsync(IDataStorage? sharedStorage)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<INodeMembership>(Membership);
        services.AddHeadlessMessaging(setup =>
        {
            setup.Options.Version = "v1";
            setup.Options.DeadNodeReconcileInterval = _ReconcileInterval;
            ConfigureStorage(setup);
        });

        // Concurrency second host on a per-instance store: override IDataStorage with the shared instance so both
        // bridges act on the same state. AddSingleton appended after AddHeadlessMessaging wins for GetRequiredService.
        if (sharedStorage is not null)
        {
            services.AddSingleton(sharedStorage);
        }

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        var storage = provider.GetRequiredService<IDataStorage>();

        if (sharedStorage is null)
        {
            await provider.ApplyMessagingSchemaAsync(AbortToken);
        }

        return (provider, storage);
    }

    private async Task _StartBridgeAsync(IServiceProvider provider)
    {
        // Resolve only the dead-owner recovery bridge from the hosted-service graph (the type is internal to
        // Coordination, so it is matched by the public IDeadOwnerRecoveryBridge marker) and start just it —
        // the Bootstrapper and processing servers stay unstarted, so no transport is required.
        var bridge = provider.GetServices<IHostedService>().First(host => host is IDeadOwnerRecoveryBridge);

        await bridge.StartAsync(AbortToken);
        _startedBridges.Add(bridge);
    }

    private async Task<(NodeIdentity Owner, Guid Published, Guid Received)> _SeedOwnedRowsAsync(
        IDataStorage storage,
        string nodeId,
        long incarnation,
        TimeSpan leaseDuration
    )
    {
        // The store stamps Owner from the membership identity at lease time, so set it to the owner-to-seed first.
        var owner = Membership.SetIdentity(nodeId, incarnation);
        var (published, received) = await _SeedLeasedRowsAsync(storage, $"{nodeId}-{incarnation}", leaseDuration);

        return (owner, published, received);
    }

    /// <summary>Seeds a published and a received retry row leased by whatever identity the storage's membership holds.</summary>
    private async Task<(Guid Published, Guid Received)> _SeedLeasedRowsAsync(
        IDataStorage storage,
        string rowPrefix,
        TimeSpan leaseDuration
    )
    {
        var published = await storage.StoreMessageAsync(
            $"{rowPrefix}-pub-{_NextId()}",
            _CreateMessage(),
            cancellationToken: AbortToken
        );
        await storage.ChangePublishStateAsync(
            published,
            StatusName.Failed,
            retryDelay: RetryDelay.Exactly(TimeSpan.Zero),
            cancellationToken: AbortToken
        );
        (await storage.LeasePublishAsync(published, leaseDuration, AbortToken))
            .Should()
            .BeTrue("the seeded published row must be actively leased before reclaim runs");

        var received = await storage.StoreReceivedMessageAsync(
            $"{rowPrefix}-rec-{_NextId()}",
            $"{rowPrefix}-grp",
            _CreateMessage(),
            AbortToken
        );
        await storage.ChangeReceiveStateAsync(
            received,
            StatusName.Failed,
            retryDelay: RetryDelay.Exactly(TimeSpan.Zero),
            cancellationToken: AbortToken
        );
        (await storage.LeaseReceiveAsync(received, leaseDuration, AbortToken))
            .Should()
            .BeTrue("the seeded received row must be actively leased before reclaim runs");

        return (published.StorageId, received.StorageId);
    }

    /// <summary>
    /// Builds a host whose membership is the real coordination provider, under <paramref name="nodeId"/> in
    /// <paramref name="cluster"/>. Its liveness thresholds are short so a node that stops beating reads as dead within
    /// about a second of the store's clock.
    /// </summary>
    private async Task<(IServiceProvider Provider, IDataStorage Storage)> _BuildCoordinatedStackAsync(
        string nodeId,
        string cluster,
        Action<HeadlessCoordinationSetupBuilder> coordinationProvider
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddHeadlessHostIdentity(options => options.HostName = nodeId);
        services.AddHeadlessCoordination(setup =>
        {
            coordinationProvider(setup);
            setup.Configure(options =>
            {
                options.ClusterName = cluster;
                options.HeartbeatInterval = TimeSpan.FromMilliseconds(200);
                options.SuspicionThreshold = TimeSpan.FromMilliseconds(500);
                options.DeadThreshold = TimeSpan.FromSeconds(1);
            });
            // Only the dead-owner bridge ever starts, and a killed node is one that stops beating, so no host beats.
            setup.DisableMembershipHeartbeat();
        });
        services.AddHeadlessMessaging(setup =>
        {
            setup.Options.Version = "v1";
            // The reconcile also bounds its snapshot read, which reaches the real store here.
            setup.Options.DeadNodeReconcileInterval = TimeSpan.FromMilliseconds(250);
            ConfigureStorage(setup);
        });

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        await provider.ApplyMessagingSchemaAsync(AbortToken);

        return (provider, provider.GetRequiredService<IDataStorage>());
    }

    private async Task<bool> _BecomesRetriableAsync(IDataStorage storage, Guid storageId, bool published)
    {
        var deadline = DateTimeOffset.UtcNow + _PositiveTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await _IsRetriableAsync(storage, storageId, published))
            {
                return true;
            }

            await Task.Delay(25, AbortToken);
        }

        return await _IsRetriableAsync(storage, storageId, published);
    }

    private async Task<bool> _IsRetriableAsync(IDataStorage storage, Guid storageId, bool published)
    {
        // GetXxxMessagesOfNeedRetryAsync only returns rows whose lease is at/under now (reclaimed or floor-expired);
        // future-leased rows are excluded, so a single call is a faithful "is this row recoverable now?" probe.
        var retriable = published
            ? await storage.GetPublishedMessagesOfNeedRetryAsync(MessageLane.Bus, AbortToken)
            : await storage.GetReceivedMessagesOfNeedRetryAsync(MessageLane.Bus, null, AbortToken);

        return retriable.Any(message => message.StorageId == storageId);
    }

    private static TimeSpan _FutureLease()
    {
        return TimeSpan.FromHours(1);
    }

    /// <summary>A negative lease: the DB stamps a deadline already in its own past, so the row seeds as expired.</summary>
    private static TimeSpan _ExpiredLease()
    {
        return TimeSpan.FromSeconds(-1);
    }

    private static long _NextId()
    {
        return Interlocked.Increment(ref _messageCounter);
    }

    private static Message _CreateMessage()
    {
        var id = $"conformance-{Interlocked.Increment(ref _messageCounter)}";

        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { MessagingHeaders.MessageId, id },
            { MessagingHeaders.MessageName, "TestMessage" },
            { MessagingHeaders.SentTime, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) },
        };

        return new Message(headers, new { Data = "test" });
    }
}
