// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Coordination;

internal sealed class MembershipService(
    IMembershipStore store,
    INodeIdProvider nodeIdProvider,
    CoordinationOptions options,
    MembershipEventSource eventSource,
    TimeProvider timeProvider,
    IHostApplicationLifetime? hostApplicationLifetime,
    ILogger<MembershipService> logger
) : INodeMembership, IDisposable
{
    private static readonly TimeSpan _MaxDefaultSelfFenceMargin = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _localMembershipLost = new();

    // The shared registration runs on the service's lifetime, not on the token of whichever caller started it: one
    // caller giving up must not cancel the attempt every other caller is awaiting, nor abandon a store write that
    // already landed before the lease is published.
    private readonly CancellationTokenSource _registrationLifetime = new();
    private readonly TaskCompletionSource _registered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _gate = new();
    private readonly Lock _timerGate = new();
    private Task<NodeIdentity>? _registration;
    private ITimer? _fenceTimer;
    private bool _disposed;
    private volatile bool _fenceEnabled;
    private int _membershipLost;

    // The identity and its fence anchor change together, so they live in one immutable lease behind a volatile
    // reference: a reader never sees an identity paired with another incarnation's anchor, and every transition
    // (renew, fail-stop, leave) is a compare-and-swap against the exact lease it observed.
    private volatile MembershipLease? _lease;

    // The latest snapshot any caller read (the heartbeat reads one every beat), kept so the dead-owner bridges can
    // reconcile from it instead of each repeating the same store read.
    private volatile LivenessRead? _lastLivenessRead;

    public string ClusterName => options.ClusterName;

    public NodeIdentity? Identity => _lease is { } lease && !_IsPastFence(lease) ? lease.Identity : null;

    public CancellationToken LocalMembershipLostToken => _localMembershipLost.Token;

    /// <summary>
    /// The fence window measured from an anchor: the node stops acting as a member this long after its registration
    /// write or its last accepted beat started, which is <see cref="CoordinationOptions.SelfFenceMargin"/> before any
    /// peer can classify it dead.
    /// </summary>
    internal TimeSpan FenceBudget => options.DeadThreshold - _ResolveSelfFenceMargin(options);

    /// <summary>
    /// The margin the fence applies: the configured value, else the smaller of 5 seconds and a sixth of
    /// <see cref="CoordinationOptions.DeadThreshold"/>. A sixth leaves five sixths of the threshold, which the
    /// DeadThreshold &gt;= 3 x HeartbeatInterval rule already makes at least 2.5 beats, so the default never violates
    /// the two-beat rule a fixed 5 seconds breaks on any threshold under 5 seconds plus two beats.
    /// </summary>
    private static TimeSpan _ResolveSelfFenceMargin(CoordinationOptions options)
    {
        if (options.SelfFenceMargin is { } configured)
        {
            return configured;
        }

        var fraction = TimeSpan.FromTicks(options.DeadThreshold.Ticks / 6);
        return fraction.Min(_MaxDefaultSelfFenceMargin);
    }

    public ValueTask<NodeIdentity> RegisterAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Task<NodeIdentity> registration;

        lock (_gate)
        {
            // A process whose membership was lost must restart with a fresh incarnation; registering again in-process
            // would hand ownership-sensitive work an identity while LocalMembershipLostToken stays cancelled.
            if (Volatile.Read(ref _membershipLost) != 0)
            {
                throw new InvalidOperationException(
                    "Local coordination membership was lost; restart the process to register a new incarnation."
                );
            }

            // Single flight: the Jobs startup gate and the heartbeat service can both ask to register, and two
            // concurrent allocations would leave this process holding an incarnation the store already superseded.
            // A failed attempt, or a lease given up by LeaveAsync, lets the next caller register afresh.
            if (
                _registration is null or { IsFaulted: true } or { IsCanceled: true }
                || (_registration.IsCompletedSuccessfully && _lease is null)
            )
            {
                _registration = _RegisterCoreAsync(_registrationLifetime.Token);
            }

            registration = _registration;
        }

        return new ValueTask<NodeIdentity>(registration.WaitAsync(cancellationToken));
    }

    private async Task<NodeIdentity> _RegisterCoreAsync(CancellationToken cancellationToken)
    {
        var nodeId = await nodeIdProvider.GetNodeIdAsync(cancellationToken).ConfigureAwait(false);
        var incarnation = await store.AllocateIncarnationAsync(nodeId, cancellationToken).ConfigureAwait(false);
        var identity = new NodeIdentity(nodeId, incarnation);
        var descriptor = new NodeDescriptor
        {
            Identity = identity,
            HostName = Environment.MachineName,
            Role = options.Role,
            Metadata = options.Metadata,
        };

        // Anchor before the write: the store stamps the initial LastBeat mid-call, so measuring from here keeps the
        // local fence no later than any peer's view of that LastBeat.
        var anchor = timeProvider.GetTimestamp();

        // UpsertDescriptorAsync durably establishes both the cold descriptor and the initial guarded liveness row, so
        // registration writes once. The heartbeat loop owns every subsequent beat.
        if (!await store.UpsertDescriptorAsync(descriptor, cancellationToken).ConfigureAwait(false))
        {
            throw new MembershipRegistrationRejectedException(identity);
        }

        var lease = new MembershipLease(identity, anchor);
        _lease = lease;
        _ArmFence(lease);
        _registered.TrySetResult();

        return identity;
    }

    public async ValueTask<bool> HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_lease is not { } lease)
        {
            return false;
        }

        if (_IsPastFence(lease))
        {
            await _FailStopLeaseAsync(lease).ConfigureAwait(false);

            return false;
        }

        // Capture BEFORE issuing the beat: the store stamps LastBeat mid-call and peers classify Dead from
        // LastBeat + DeadThreshold, so anchoring the next fence to the call's return would grant this node overhang
        // equal to the beat's completion latency.
        var started = timeProvider.GetTimestamp();
        var accepted = await store.HeartbeatAsync(lease.Identity, cancellationToken).ConfigureAwait(false);

        if (!accepted)
        {
            if (_TryReplaceLease(lease, replacement: null))
            {
                await _SignalLocalMembershipLostAsync(lease.Identity).ConfigureAwait(false);
            }

            return false;
        }

        // Crossing the deadline is terminal even when a slow beat lands afterwards: Identity already read null to
        // stamp sites, and a node that flapped back would be stamping again after it told callers it stopped.
        if (_IsPastFence(lease))
        {
            await _FailStopLeaseAsync(lease).ConfigureAwait(false);

            return false;
        }

        // Renew only the lease this beat started from; a fail-stop, leave, or loss that landed while the beat was in
        // flight must not be undone by its late success.
        var renewed = lease with
        {
            AnchorTimestamp = started,
        };

        if (!_TryReplaceLease(lease, renewed))
        {
            return false;
        }

        _ArmFence(renewed);

        return true;
    }

    public async ValueTask LeaveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Leave on the held lease, not Identity: a node past its fence deadline whose timer has not fired yet reads a
        // null Identity, but still owes peers the explicit departure so they react to NodeLeft instead of waiting out
        // DeadThreshold. The store's leave is a guarded no-op for an incarnation it already superseded.
        if (_lease is not { } lease)
        {
            return;
        }

        await store.LeaveAsync(lease.Identity, cancellationToken).ConfigureAwait(false);
        _TryReplaceLease(lease, replacement: null);
    }

    /// <summary>
    /// Turns the self-fence on. The heartbeat service calls this: without a beat source a host would only ever reach
    /// the deadline, so a host built with <c>DisableMembershipHeartbeat()</c> never fences itself.
    /// </summary>
    internal void EnableSelfFence()
    {
        _fenceEnabled = true;

        if (_lease is { } lease)
        {
            _ArmFence(lease);
        }
    }

    /// <summary>Completes when this process has registered, through any caller.</summary>
    internal Task WaitForRegistrationAsync(CancellationToken cancellationToken)
    {
        return _registered.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the held identity and the fence budget left on it, even past the deadline (where <see cref="Identity"/>
    /// already reads null), so the heartbeat loop can time its beats and fail-stop an expired lease.
    /// </summary>
    internal bool TryGetFence(out NodeIdentity identity, out TimeSpan remaining)
    {
        if (_lease is not { } lease)
        {
            identity = default;
            remaining = TimeSpan.Zero;

            return false;
        }

        identity = lease.Identity;
        remaining = _FenceRemaining(lease);

        return true;
    }

    internal async ValueTask FailStopAsync(NodeIdentity identity)
    {
        if (_lease is not { } lease || lease.Identity != identity)
        {
            return;
        }

        await _FailStopLeaseAsync(lease).ConfigureAwait(false);
    }

    // Fails the exact lease the caller judged expired. A renewal keeps the same identity under a new anchor, so
    // matching by identity alone would let a deadline seen on the old lease fence the renewed one.
    private async ValueTask _FailStopLeaseAsync(MembershipLease lease)
    {
        if (!_TryReplaceLease(lease, replacement: null))
        {
            return;
        }

        await _SignalLocalMembershipLostAsync(lease.Identity).ConfigureAwait(false);
    }

    public async ValueTask<bool> IsAliveAsync(NodeIdentity identity, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Targeted single-node read instead of a full cluster snapshot + filter: a node is alive only when its
        // current-generation liveness classifies as Alive. Absent (null) and any non-Alive state map to false,
        // exactly matching the prior snapshot-based predicate.
        var state = await store.ReadNodeLivenessAsync(identity, cancellationToken).ConfigureAwait(false);

        return state is NodeLivenessState.Alive;
    }

    public async ValueTask<IReadOnlyList<NodeIdentity>> GetLiveNodesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Targeted live-node read: the store serves Alive-only current-generation identities directly (Redis
        // from the :live sorted set, relational from a filtered query) instead of materializing the full
        // snapshot and filtering it here.
        return await store.ReadLiveNodesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<NodeLivenessSnapshot>> GetLivenessSnapshotAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Stamp before the read, so the recorded age never understates how old the data can be.
        var readStartedAt = timeProvider.GetTimestamp();

        // The store SPI returns snapshots already sorted by identity; no service-side re-sort is needed.
        var snapshot = await store.ReadLivenessAsync(cancellationToken).ConfigureAwait(false);

        _RecordLivenessRead(new LivenessRead(snapshot, readStartedAt));

        return snapshot;
    }

    /// <summary>
    /// Returns the latest liveness snapshot read through this service when that read started after
    /// <paramref name="startedAfter"/> and no longer ago than <paramref name="maxAge"/> and one
    /// <see cref="CoordinationOptions.HeartbeatInterval"/>, whichever is shorter.
    /// </summary>
    /// <remarks>
    /// A healthy heartbeat refreshes it every beat, so a caller polling slower than the beat reads no store at all.
    /// A stalled or failing heartbeat lets it age out, and the caller then reads the store itself. Requiring a read
    /// newer than <paramref name="startedAfter"/> (the caller's previous decision) means a caller never decides twice
    /// from the same read: on a clock that does not move, the age check alone would let one read be reused forever.
    /// </remarks>
    /// <param name="maxAge">The caller's own bound on the read's age.</param>
    /// <param name="startedAfter">A registered-clock timestamp the read must have started strictly after.</param>
    /// <param name="snapshot">The reusable snapshot.</param>
    /// <param name="startedAt">When the returned read started, for the caller's next <paramref name="startedAfter"/>.</param>
    internal bool TryGetRecentLivenessSnapshot(
        TimeSpan maxAge,
        long startedAfter,
        [NotNullWhen(true)] out IReadOnlyList<NodeLivenessSnapshot>? snapshot,
        out long startedAt
    )
    {
        var limit = maxAge < options.HeartbeatInterval ? maxAge : options.HeartbeatInterval;

        if (
            _lastLivenessRead is { } read
            && read.StartedAt > startedAfter
            && timeProvider.GetElapsedTime(read.StartedAt) <= limit
        )
        {
            snapshot = read.Snapshot;
            startedAt = read.StartedAt;

            return true;
        }

        snapshot = null;
        startedAt = 0;

        return false;
    }

    private void _RecordLivenessRead(LivenessRead read)
    {
        // Concurrent readers can finish out of order; keep the read that started last so an older one finishing late
        // never replaces newer data.
        var current = _lastLivenessRead;

        while (current is null || read.StartedAt >= current.StartedAt)
        {
            var observed = Interlocked.CompareExchange(ref _lastLivenessRead, read, current);

            if (ReferenceEquals(observed, current))
            {
                return;
            }

            current = observed;
        }
    }

    public IAsyncEnumerable<NodeMembershipEvent> WatchAsync(CancellationToken cancellationToken = default)
    {
        return eventSource.WatchAsync(cancellationToken);
    }

    private TimeSpan _FenceRemaining(MembershipLease lease)
    {
        return FenceBudget - timeProvider.GetElapsedTime(lease.AnchorTimestamp);
    }

    private bool _IsPastFence(MembershipLease lease)
    {
        return _fenceEnabled && _FenceRemaining(lease) <= TimeSpan.Zero;
    }

    private bool _TryReplaceLease(MembershipLease expected, MembershipLease? replacement)
    {
        return ReferenceEquals(Interlocked.CompareExchange(ref _lease, replacement, expected), expected);
    }

    private void _ArmFence(MembershipLease lease)
    {
        if (!_fenceEnabled)
        {
            return;
        }

        var due = _FenceRemaining(lease).Max(TimeSpan.Zero);

        lock (_timerGate)
        {
            // A beat or registration still in flight when the host disposes this service must not bring back a
            // timer that would fail-stop, publish, and stop the application after shutdown.
            if (_disposed)
            {
                return;
            }

            _fenceTimer ??= timeProvider.CreateTimer(
                static state => ((MembershipService)state!)._OnFenceTimer(),
                this,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan
            );

            _fenceTimer.Change(due, Timeout.InfiniteTimeSpan);
        }
    }

    private void _OnFenceTimer()
    {
        if (_lease is not { } lease)
        {
            return;
        }

        // A beat accepted after the timer was armed re-arms it, but a callback already queued can still run with the
        // old due time; only a lease that is actually past its deadline is failed.
        if (!_IsPastFence(lease))
        {
            _ArmFence(lease);

            return;
        }

        _ = _FailStopFromTimerAsync(lease);
    }

    private async Task _FailStopFromTimerAsync(MembershipLease lease)
    {
        try
        {
            await _FailStopLeaseAsync(lease).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A timer callback has no caller to propagate to; the loss itself already happened before any throw.
            logger.SelfFenceFailed(ex, lease.Identity);
        }
    }

    private async ValueTask _SignalLocalMembershipLostAsync(NodeIdentity identity)
    {
        if (Interlocked.Exchange(ref _membershipLost, 1) != 0)
        {
            return;
        }

        try
        {
            await _localMembershipLost.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Defensive: Dispose() can race this at-most-once path during shutdown. The Interlocked guard
            // above still ensures we proceed once; a disposed CTS just means no token observers remain.
        }

        eventSource.Publish(new LocalMembershipLost(identity));
        logger.LocalMembershipLost(identity);

        if (options.MembershipLostBehavior != MembershipLostBehavior.StopApplication)
        {
            return;
        }

        try
        {
            hostApplicationLifetime?.StopApplication();
        }
        catch (Exception ex)
        {
            logger.StopApplicationFailed(ex, identity);
        }
    }

    public void Dispose()
    {
        lock (_timerGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _fenceTimer?.Dispose();
            _fenceTimer = null;
        }

        _registrationLifetime.Cancel();
        _registrationLifetime.Dispose();
        _localMembershipLost.Dispose();
    }

    private sealed record MembershipLease(NodeIdentity Identity, long AnchorTimestamp);

    /// <summary>A liveness snapshot and the registered-clock timestamp taken just before its read started.</summary>
    private sealed record LivenessRead(IReadOnlyList<NodeLivenessSnapshot> Snapshot, long StartedAt);
}
