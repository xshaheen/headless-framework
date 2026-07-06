// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

/// <summary>
/// Cross-provider end-to-end conformance for messaging dead-owner recovery. Hosts a real
/// <c>DeadOwnerRecoveryBridge&lt;MessagingDeadOwnerReclaimer&gt;</c> (resolved through the messaging DI graph, since
/// the bridge type is internal to Coordination.Core) against a real provider store and a
/// <see cref="ControlledNodeMembership"/>, then asserts outbox/inbox row outcomes.
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
    protected virtual Task ResetStorageAsync(IDataStorage storage) => Task.CompletedTask;

    /// <summary>
    /// Whether a second concurrent bridge host shares the <em>same</em> <see cref="IDataStorage"/> instance
    /// (InMemory, whose state is per-instance) instead of a separate instance over the same database (SQL providers
    /// naturally share through the connection string).
    /// </summary>
    protected virtual bool SharesStorageInstanceForConcurrency => false;

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

    public virtual async Task should_reclaim_dead_owner_published_and_received_rows()
    {
        // AE2 / R3: a Dead owner's in-flight rows are re-dispatched, not skipped.
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

    public virtual async Task should_not_reclaim_suspected_owner_rows()
    {
        // AE1 / R2: a Suspected owner is never reclaimed through watch or reconcile.
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

    public virtual async Task should_reclaim_once_when_surfaced_by_both_event_and_reconcile()
    {
        // AE3 / R7: a Dead owner surfaced by both a NodeLeft event and the reconcile is reclaimed (dedup, no thrash).
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

    public virtual async Task should_fence_a_restarted_incarnation()
    {
        // AE5 / R12: node@N dead, node@N+1 live (restart) → only the dead incarnation's rows are reclaimed.
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
        // AE4 / R4: a dead owner that aged out of the snapshot before reclaim still recovers via LockedUntil expiry.
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

    public virtual async Task should_reclaim_once_under_two_concurrent_bridges()
    {
        // Concurrency: two bridge hosts reclaiming the same Dead owner → rows reclaimed, no duplicate-thrash error
        // (the owner-scoped conditional UPDATE is idempotent — KTD3).
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
            await provider.GetRequiredService<IStorageInitializer>().InitializeAsync(AbortToken);
        }

        return (provider, storage);
    }

    private async Task _StartBridgeAsync(IServiceProvider provider)
    {
        // Resolve only the dead-owner recovery bridge from the hosted-service graph (the type is internal to
        // Coordination.Core, so it is matched by the public IDeadOwnerRecoveryBridge marker) and start just it —
        // the Bootstrapper and processing servers stay unstarted, so no transport is required.
        var bridge = provider.GetServices<IHostedService>().First(host => host is IDeadOwnerRecoveryBridge);

        await bridge.StartAsync(AbortToken);
        _startedBridges.Add(bridge);
    }

    private async Task<(NodeIdentity Owner, Guid Published, Guid Received)> _SeedOwnedRowsAsync(
        IDataStorage storage,
        string nodeId,
        long incarnation,
        DateTime lockedUntil
    )
    {
        // The store stamps Owner from the membership identity at lease time, so set it to the owner-to-seed first.
        var owner = Membership.SetIdentity(nodeId, incarnation);

        var published = await storage.StoreMessageAsync(
            $"{nodeId}-{incarnation}-pub-{_NextId()}",
            _CreateMessage(),
            cancellationToken: AbortToken
        );
        await storage.ChangePublishStateAsync(
            published,
            StatusName.Failed,
            nextRetryAt: _Now().AddSeconds(-1),
            cancellationToken: AbortToken
        );
        (await storage.LeasePublishAsync(published, lockedUntil, AbortToken))
            .Should()
            .BeTrue("the seeded published row must be actively leased before reclaim runs");

        var received = await storage.StoreReceivedMessageAsync(
            $"{nodeId}-{incarnation}-rec-{_NextId()}",
            $"{nodeId}-grp",
            _CreateMessage(),
            AbortToken
        );
        await storage.ChangeReceiveStateAsync(
            received,
            StatusName.Failed,
            nextRetryAt: _Now().AddSeconds(-1),
            cancellationToken: AbortToken
        );
        (await storage.LeaseReceiveAsync(received, lockedUntil, AbortToken))
            .Should()
            .BeTrue("the seeded received row must be actively leased before reclaim runs");

        return (owner, published.StorageId, received.StorageId);
    }

    private async Task<bool> _BecomesRetriableAsync(IDataStorage storage, Guid storageId, bool published)
    {
        var deadline = DateTime.UtcNow + _PositiveTimeout;

        while (DateTime.UtcNow < deadline)
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
            ? await storage.GetPublishedMessagesOfNeedRetryAsync(AbortToken)
            : await storage.GetReceivedMessagesOfNeedRetryAsync(AbortToken);

        return retriable.Any(message => message.StorageId == storageId);
    }

    private static DateTime _Now() => TimeProvider.System.GetUtcNow().UtcDateTime;

    private static DateTime _FutureLease() => _Now().AddHours(1);

    private static DateTime _ExpiredLease() => _Now().AddSeconds(-1);

    private static long _NextId() => Interlocked.Increment(ref _messageCounter);

    private static Message _CreateMessage()
    {
        var id = $"conformance-{Interlocked.Increment(ref _messageCounter)}";

        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { MessagingHeaders.MessageId, id },
            { MessagingHeaders.MessageName, "TestMessage" },
            { MessagingHeaders.SentTime, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) },
        };

        return new Message(headers, new { Data = "test" });
    }
}
