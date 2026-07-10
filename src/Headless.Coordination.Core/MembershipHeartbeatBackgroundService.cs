// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Coordination;

internal sealed class MembershipHeartbeatBackgroundService(
    MembershipService membership,
    MembershipEventSource eventSource,
    CoordinationOptions options,
    TimeProvider timeProvider,
    ILogger<MembershipHeartbeatBackgroundService> logger
) : BackgroundService
{
    private Dictionary<NodeIdentity, NodeLivenessState> _previous = [];
    private long _lastConfirmedHeartbeatTimestamp = timeProvider.GetTimestamp();
    private NodeIdentity? _trackedIdentity;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Stop issuing heartbeats once local membership is lost (StopMembershipOnly) as well as on host stop.
        using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            membership.LocalMembershipLostToken
        );
        var loopToken = loopCts.Token;

        if (membership.Identity is null)
        {
            if (!await _TryRegisterWithRetryAsync(stoppingToken).ConfigureAwait(false))
            {
                return;
            }
        }

        while (!loopToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(loopToken).ConfigureAwait(false);
                await timeProvider.Delay(options.HeartbeatInterval, loopToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // Best-effort graceful leave under a bounded budget so a store outage can't hang host shutdown.
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5), timeProvider);
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
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.MembershipRegistrationRetry(ex, attempt, maxAttempts);
                await timeProvider.Delay(delay, cancellationToken).ConfigureAwait(false);
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

        if (membership.Identity is { } identity)
        {
            if (_trackedIdentity != identity)
            {
                _trackedIdentity = identity;
                _lastConfirmedHeartbeatTimestamp = timeProvider.GetTimestamp();
            }

            var elapsed = timeProvider.GetElapsedTime(_lastConfirmedHeartbeatTimestamp);
            var remaining = options.DeadThreshold - elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                await membership.FailStopAsync(identity).ConfigureAwait(false);
                return;
            }

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

                _lastConfirmedHeartbeatTimestamp = timeProvider.GetTimestamp();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested)
            {
                logger.HeartbeatFailed(ex, identity, options.DeadThreshold, options.DeadThreshold);
                await membership.FailStopAsync(identity).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                elapsed = timeProvider.GetElapsedTime(_lastConfirmedHeartbeatTimestamp);
                logger.HeartbeatFailed(ex, identity, elapsed, options.DeadThreshold);

                if (elapsed >= options.DeadThreshold)
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
