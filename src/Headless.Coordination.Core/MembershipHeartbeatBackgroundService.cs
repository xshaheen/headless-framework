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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (membership.Identity is null)
        {
            await membership.RegisterAsync(stoppingToken).ConfigureAwait(false);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            await timeProvider.Delay(options.HeartbeatInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (membership.Identity is not null && !await membership.HeartbeatAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

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
