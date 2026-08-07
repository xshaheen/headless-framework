// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;

namespace Headless.Messaging.Processor;

internal interface IRetryDispatcher
{
    ValueTask DispatchPublishedAsync(MediumMessage message, CancellationToken cancellationToken = default);

    ValueTask DispatchReceivedAsync(MediumMessage message, CancellationToken cancellationToken = default);
}

internal sealed class RetryDispatchAttempt
{
    private readonly IGracefulLeaseReleaseStorage _storage;
    private readonly MessageType _direction;
    private int _state = (int)AttemptState.Claimed;

    private RetryDispatchAttempt(
        IGracefulLeaseReleaseStorage storage,
        MessageType direction,
        MessageLeaseIdentity identity
    )
    {
        _storage = storage;
        _direction = direction;
        Identity = identity;
    }

    public MessageLeaseIdentity Identity { get; }

    public static RetryDispatchAttempt? TryCreate(IDataStorage storage, MessageType direction, MediumMessage message)
    {
        if (storage is not IGracefulLeaseReleaseStorage releaser || message.LockedUntil is not { } lockedUntil)
        {
            return null;
        }

        return new RetryDispatchAttempt(
            releaser,
            direction,
            new MessageLeaseIdentity(message.StorageId, message.Owner, lockedUntil, message.Lane)
        );
    }

    public static async ValueTask ReleaseClaimedBatchAsync(
        IDataStorage storage,
        MessageType direction,
        IEnumerable<MediumMessage> messages
    )
    {
        if (storage is not IGracefulLeaseReleaseStorage releaser)
        {
            return;
        }

        var identities = messages
            .Where(message => message.LockedUntil.HasValue)
            .Select(message => new MessageLeaseIdentity(
                message.StorageId,
                message.Owner,
                message.LockedUntil!.Value,
                message.Lane
            ))
            .ToArray();
        if (identities.Length == 0)
        {
            return;
        }

        _ = direction switch
        {
            MessageType.Publish => await releaser
                .ReleasePublishedLeasesAsync(identities, CancellationToken.None)
                .ConfigureAwait(false),
            MessageType.Subscribe => await releaser
                .ReleaseReceivedLeasesAsync(identities, CancellationToken.None)
                .ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported retry direction '{direction}'."),
        };
    }

    public static async ValueTask ReleaseAbandonedBatchAsync(IEnumerable<RetryDispatchAttempt> attempts)
    {
        var batches = new Dictionary<IGracefulLeaseReleaseStorage, ReleaseBatch>(ReferenceEqualityComparer.Instance);
        foreach (var attempt in attempts)
        {
            if (
                Interlocked.CompareExchange(ref attempt._state, (int)AttemptState.Abandoned, (int)AttemptState.Queued)
                != (int)AttemptState.Queued
            )
            {
                continue;
            }

            if (!batches.TryGetValue(attempt._storage, out var batch))
            {
                batch = new ReleaseBatch();
                batches.Add(attempt._storage, batch);
            }

            var identities = attempt._direction switch
            {
                MessageType.Publish => batch.Published,
                MessageType.Subscribe => batch.Received,
                _ => throw new InvalidOperationException($"Unsupported retry direction '{attempt._direction}'."),
            };
            identities.Add(attempt.Identity);
        }

        foreach (var (storage, batch) in batches)
        {
            if (batch.Published.Count > 0)
            {
                _ = await storage
                    .ReleasePublishedLeasesAsync(batch.Published, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            if (batch.Received.Count > 0)
            {
                _ = await storage
                    .ReleaseReceivedLeasesAsync(batch.Received, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
    }

    public bool TryQueue()
    {
        return Interlocked.CompareExchange(ref _state, (int)AttemptState.Queued, (int)AttemptState.Claimed)
            == (int)AttemptState.Claimed;
    }

    public bool TryStart()
    {
        if (
            Interlocked.CompareExchange(ref _state, (int)AttemptState.Running, (int)AttemptState.Queued)
            == (int)AttemptState.Queued
        )
        {
            return true;
        }

        return Interlocked.CompareExchange(ref _state, (int)AttemptState.Running, (int)AttemptState.Claimed)
            == (int)AttemptState.Claimed;
    }

    public ValueTask AbandonClaimedAsync()
    {
        return _TransitionAndReleaseAsync(AttemptState.Claimed, AttemptState.Abandoned);
    }

    public ValueTask AbandonQueuedAsync()
    {
        return _TransitionAndReleaseAsync(AttemptState.Queued, AttemptState.Abandoned);
    }

    public async ValueTask AbandonAsync()
    {
        await AbandonClaimedAsync().ConfigureAwait(false);
        await AbandonQueuedAsync().ConfigureAwait(false);
    }

    public ValueTask CompleteAsync(bool leaseClearedByTransition = false)
    {
        if (leaseClearedByTransition)
        {
            _ = Interlocked.CompareExchange(ref _state, (int)AttemptState.Completed, (int)AttemptState.Running);
            return ValueTask.CompletedTask;
        }

        return _TransitionAndReleaseAsync(AttemptState.Running, AttemptState.Completed);
    }

    private async ValueTask _TransitionAndReleaseAsync(AttemptState expected, AttemptState next)
    {
        if (Interlocked.CompareExchange(ref _state, (int)next, (int)expected) != (int)expected)
        {
            return;
        }

        _ = _direction switch
        {
            MessageType.Publish => await _storage
                .ReleasePublishedLeaseAsync(Identity, CancellationToken.None)
                .ConfigureAwait(false),
            MessageType.Subscribe => await _storage
                .ReleaseReceivedLeaseAsync(Identity, CancellationToken.None)
                .ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported retry direction '{_direction}'."),
        };
    }

    private enum AttemptState
    {
        Claimed,
        Queued,
        Running,
        Completed,
        Abandoned,
    }

    private sealed class ReleaseBatch
    {
        public List<MessageLeaseIdentity> Published { get; } = [];

        public List<MessageLeaseIdentity> Received { get; } = [];
    }
}
