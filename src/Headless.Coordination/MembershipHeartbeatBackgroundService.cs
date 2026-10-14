// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Coordination;

/// <summary>Registers this node, heartbeats it, publishes membership events, and leaves on shutdown.</summary>
/// <remarks>
/// The beat loop starts in <see cref="StartingAsync"/>, before any hosted service's <c>StartAsync</c>, and waits there
/// for whichever caller registers first (the Jobs startup gate, or this service's own register-if-null in
/// <c>ExecuteAsync</c>). The self-fence is anchored at that registration, so a slow <c>StartingAsync</c> in a later
/// service cannot outlast <see cref="CoordinationOptions.DeadThreshold"/> while nothing beats.
/// </remarks>
/// <param name="timeProvider">
/// The registered clock: paces beats and bounds each beat by the fence budget left.
/// </param>
/// <param name="realTimeProvider">
/// The clock for waits that ride out a store failure: the registration back-off and the bounded leave on stop.
/// Registration passes <see cref="TimeProvider.System"/>; tests pass a fake to step through those waits.
/// </param>
internal sealed class MembershipHeartbeatBackgroundService(
    MembershipService membership,
    MembershipEventSource eventSource,
    CoordinationOptions options,
    TimeProvider timeProvider,
    TimeProvider realTimeProvider,
    ILogger<MembershipHeartbeatBackgroundService> logger
) : BackgroundService, IHostedLifecycleService
{
    private readonly CancellationTokenSource _loopCts = new();
    private Dictionary<NodeIdentity, NodeLivenessState> _previous = [];
    private Task? _loop;
    private int _loopStarted;
    private int _disposed;

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _StartLoop();

        return Task.CompletedTask;
    }

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A host that never calls the lifecycle hooks (or a test driving StartAsync alone) still gets the loop.
        _StartLoop();

        // Register-if-null for hosts without the Jobs startup gate. A membership already lost in this process is
        // never re-registered: the INodeMembership contract is that the process restarts with a new incarnation.
        if (membership.Identity is null && !membership.LocalMembershipLostToken.IsCancellationRequested)
        {
            if (!await _TryRegisterWithRetryAsync(stoppingToken).ConfigureAwait(false))
            {
                return;
            }
        }

        try
        {
            await _loop!.WaitAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host stop: StopAsync cancels the loop itself.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            await _loopCts.CancelAsync().ConfigureAwait(false);
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // Best-effort graceful leave under a bounded budget so a store outage can't hang host shutdown.
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5), realTimeProvider);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await membership.LeaveAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            logger.LeaveOnShutdownTimedOut();
        }
        catch (Exception ex)
        {
            logger.LeaveOnShutdownFailed(ex);
        }
    }

    public override void Dispose()
    {
        // The container disposes this instance twice: once as the singleton and once through the IHostedService
        // factory that returns the same instance.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _loopCts.Cancel();
        _loopCts.Dispose();
        base.Dispose();
    }

    private void _StartLoop()
    {
        if (Interlocked.Exchange(ref _loopStarted, 1) != 0)
        {
            return;
        }

        // Only a host that beats may fence itself; this service is that beat source.
        membership.EnableSelfFence();
        _loop = _RunLoopAsync(_loopCts.Token);
    }

    private async Task _RunLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            await membership.WaitForRegistrationAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Stop issuing heartbeats once local membership is lost (StopMembershipOnly) as well as on host stop.
        using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            membership.LocalMembershipLostToken
        );
        var loopToken = loopCts.Token;

        while (!loopToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(loopToken).ConfigureAwait(false);
                await timeProvider.Delay(_NextBeatDelay(), loopToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // A beat interval, but never past the fence deadline: after failed beats the next attempt lands while the
    // node can still renew, and at the deadline the next tick fail-stops a lease the timer has not reached yet.
    private TimeSpan _NextBeatDelay()
    {
        if (!membership.TryGetFence(out _, out var remaining) || remaining >= options.HeartbeatInterval)
        {
            return options.HeartbeatInterval;
        }

        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private async Task<bool> _TryRegisterWithRetryAsync(CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;
        var delay = TimeSpan.FromMilliseconds(200);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await membership.RegisterAsync(cancellationToken).ConfigureAwait(false);

                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (MembershipRegistrationRejectedException)
            {
                // A duplicate NodeId is a configuration error, not a transient store failure: retrying would only
                // race the other process for the generation again.
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.MembershipRegistrationRetry(ex, attempt, maxAttempts);
                await realTimeProvider.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 5000));
            }
            catch (Exception ex) when (options.MembershipLostBehavior == MembershipLostBehavior.StopMembershipOnly)
            {
                logger.MembershipRegistrationFailed(ex, maxAttempts);

                return false;
            }
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (membership.TryGetFence(out var identity, out var remaining))
        {
            if (remaining <= TimeSpan.Zero)
            {
                await membership.FailStopAsync(identity).ConfigureAwait(false);
                return;
            }

            // Bound the beat by the fence budget left: a hung store call must not outlive the instant the node stops
            // being a member.
            using var timeoutCts = new CancellationTokenSource(remaining, timeProvider);
            using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token
            );

            try
            {
                var accepted = await membership
                    .HeartbeatAsync(heartbeatCts.Token)
                    .AsTask()
                    .WaitAsync(heartbeatCts.Token)
                    .ConfigureAwait(false);
                if (!accepted)
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested)
            {
                logger.HeartbeatFailed(ex, identity, TimeSpan.Zero, membership.FenceBudget);
                await membership.FailStopAsync(identity).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                membership.TryGetFence(out _, out remaining);
                logger.HeartbeatFailed(ex, identity, remaining, membership.FenceBudget);

                if (remaining <= TimeSpan.Zero)
                {
                    await membership.FailStopAsync(identity).ConfigureAwait(false);
                }

                return;
            }
        }

        try
        {
            var snapshots = await membership.GetLivenessSnapshotAsync(cancellationToken).ConfigureAwait(false);
            _PublishDiff(snapshots);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LivenessReadFailed(ex);
        }
    }

    private void _PublishDiff(IReadOnlyList<NodeLivenessSnapshot> snapshots)
    {
        var current = snapshots.ToDictionary(static snapshot => snapshot.Identity, static snapshot => snapshot.State);

        foreach (var (identity, previousState) in _previous)
        {
            if (!current.ContainsKey(identity) && previousState != NodeLivenessState.Dead)
            {
                eventSource.Publish(new NodeLeft(identity));
            }
        }

        foreach (var snapshot in snapshots)
        {
            if (!_previous.TryGetValue(snapshot.Identity, out var previousState))
            {
                _PublishFirstObservation(snapshot);

                continue;
            }

            _PublishTransition(snapshot.Identity, previousState, snapshot.State);
        }

        _previous = current;
    }

    private void _PublishFirstObservation(NodeLivenessSnapshot snapshot)
    {
        switch (snapshot.State)
        {
            case NodeLivenessState.Alive:
                eventSource.Publish(new NodeJoined(snapshot.Identity));
                break;

            case NodeLivenessState.Suspected:
                eventSource.Publish(new NodeSuspected(snapshot.Identity));
                break;

            case NodeLivenessState.Dead:
                eventSource.Publish(new NodeLeft(snapshot.Identity));
                break;
        }
    }

    private void _PublishTransition(
        NodeIdentity identity,
        NodeLivenessState previousState,
        NodeLivenessState currentState
    )
    {
        if (previousState == currentState)
        {
            return;
        }

        if (previousState == NodeLivenessState.Suspected && currentState == NodeLivenessState.Alive)
        {
            eventSource.Publish(new NodeRecovered(identity));

            return;
        }

        if (previousState == NodeLivenessState.Alive && currentState == NodeLivenessState.Suspected)
        {
            eventSource.Publish(new NodeSuspected(identity));

            return;
        }

        if (currentState == NodeLivenessState.Dead)
        {
            eventSource.Publish(new NodeLeft(identity));

            return;
        }

        if (previousState == NodeLivenessState.Dead && currentState == NodeLivenessState.Alive)
        {
            eventSource.Publish(new NodeJoined(identity));
        }
    }
}
