// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
    private readonly TaskCompletionSource _registered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _gate = new();
    private readonly Lock _timerGate = new();
    private Task<NodeIdentity>? _registration;
    private ITimer? _fenceTimer;
    private volatile bool _fenceEnabled;
    private int _membershipLost;

    // The identity and its fence anchor change together, so they live in one immutable lease behind a volatile
    // reference: a reader never sees an identity paired with another incarnation's anchor, and every transition
    // (renew, fail-stop, leave) is a compare-and-swap against the exact lease it observed.
    private volatile MembershipLease? _lease;

    public NodeIdentity? Identity => _lease is { } lease && !_IsPastFence(lease) ? lease.Identity : null;

    public CancellationToken LocalMembershipLostToken => _localMembershipLost.Token;

    /// <summary>
    /// The fence window measured from an anchor: the node stops acting as a member this long after its registration
    /// write or its last accepted beat started, which is <see cref="CoordinationOptions.SelfFenceMargin"/> before any
    /// peer can classify it dead.
    /// </summary>
    internal TimeSpan FenceBudget => options.DeadThreshold - ResolveSelfFenceMargin(options);

    /// <summary>
    /// The margin the fence applies: the configured value, else the smaller of 5 seconds and a sixth of
    /// <see cref="CoordinationOptions.DeadThreshold"/>. A sixth leaves five sixths of the threshold, which the
    /// DeadThreshold &gt;= 3 x HeartbeatInterval rule already makes at least 2.5 beats, so the default never violates
    /// the two-beat rule a fixed 5 seconds breaks on any threshold under 5 seconds plus two beats.
    /// </summary>
    internal static TimeSpan ResolveSelfFenceMargin(CoordinationOptions options)
    {
        if (options.SelfFenceMargin is { } configured)
        {
            return configured;
        }

        var fraction = TimeSpan.FromTicks(options.DeadThreshold.Ticks / 6);
        return fraction < _MaxDefaultSelfFenceMargin ? fraction : _MaxDefaultSelfFenceMargin;
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
                _registration = _RegisterCoreAsync(cancellationToken);
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
            await FailStopAsync(lease.Identity).ConfigureAwait(false);

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
            await FailStopAsync(lease.Identity).ConfigureAwait(false);

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

        if (Identity is not { } identity)
        {
            return;
        }

        await store.LeaveAsync(identity, cancellationToken).ConfigureAwait(false);

        if (_lease is { } lease && lease.Identity == identity)
        {
            _TryReplaceLease(lease, replacement: null);
        }
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
        if (_lease is not { } lease || lease.Identity != identity || !_TryReplaceLease(lease, replacement: null))
        {
            return;
        }

        await _SignalLocalMembershipLostAsync(identity).ConfigureAwait(false);
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

        // The store SPI returns snapshots already sorted by identity; no service-side re-sort is needed.
        return await store.ReadLivenessAsync(cancellationToken).ConfigureAwait(false);
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

        var due = _FenceRemaining(lease);

        if (due < TimeSpan.Zero)
        {
            due = TimeSpan.Zero;
        }

        lock (_timerGate)
        {
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

        _ = _FailStopFromTimerAsync(lease.Identity);
    }

    private async Task _FailStopFromTimerAsync(NodeIdentity identity)
    {
        try
        {
            await FailStopAsync(identity).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A timer callback has no caller to propagate to; the loss itself already happened before any throw.
            logger.SelfFenceFailed(ex, identity);
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
            _fenceTimer?.Dispose();
            _fenceTimer = null;
        }

        _localMembershipLost.Dispose();
    }

    private sealed record MembershipLease(NodeIdentity Identity, long AnchorTimestamp);
}
