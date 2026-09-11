// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;

namespace Headless.Messaging.Processor;

internal interface IRetryDispatcher
{
    ValueTask DispatchPublishedAsync(MediumMessage message, CancellationToken cancellationToken = default);

    ValueTask<bool> DispatchReceivedAsync(MediumMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches a claimed received retry. <paramref name="onAbandonedBeforeExecution"/> runs exactly
    /// once if the attempt is abandoned before execution starts (refused, drained during quiesce, or
    /// cancelled while queued); it never runs once the executor has taken ownership of the attempt.
    /// </summary>
    ValueTask<bool> DispatchReceivedAsync(
        MediumMessage message,
        Action? onAbandonedBeforeExecution,
        CancellationToken cancellationToken = default
    );
}

internal sealed class RetryDispatchAttempt
{
    private readonly IGracefulLeaseReleaseStorage _storage;
    private readonly MessageType _direction;
    private readonly Action? _onAbandonedBeforeExecution;
    private int _state = (int)AttemptState.Claimed;

    private RetryDispatchAttempt(
        IGracefulLeaseReleaseStorage storage,
        MessageType direction,
        MessageLeaseIdentity identity,
        Action? onAbandonedBeforeExecution
    )
    {
        _storage = storage;
        _direction = direction;
        Identity = identity;
        _onAbandonedBeforeExecution = onAbandonedBeforeExecution;
    }

    public MessageLeaseIdentity Identity { get; }

    /// <summary>Creates a durable attempt for a claimed retry, or <see langword="null"/> when the store cannot release exact leases.</summary>
    /// <param name="storage">The storage that owns the claimed lease.</param>
    /// <param name="direction">The message direction of the claim.</param>
    /// <param name="message">The claimed message.</param>
    /// <param name="onAbandonedBeforeExecution">
    /// Invoked exactly once when the attempt transitions to Abandoned from Claimed or Queued, i.e.
    /// before execution ever started. Never invoked after <see cref="TryStart"/> succeeded.
    /// </param>
    public static RetryDispatchAttempt? TryCreate(
        IDataStorage storage,
        MessageType direction,
        MediumMessage message,
        Action? onAbandonedBeforeExecution = null
    )
    {
        if (storage is not IGracefulLeaseReleaseStorage releaser || message.LockedUntil is not { } lockedUntil)
        {
            return null;
        }

        return new RetryDispatchAttempt(
            releaser,
            direction,
            new MessageLeaseIdentity(
                message.StorageId,
                message.Owner,
                lockedUntil,
                message.Lane,
                message.InboxAttemptFence
            ),
            onAbandonedBeforeExecution
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
                message.Lane,
                message.InboxAttemptFence
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
        var abandoned = new List<RetryDispatchAttempt>();
        foreach (var attempt in attempts)
        {
            if (
                Interlocked.CompareExchange(ref attempt._state, (int)AttemptState.Abandoned, (int)AttemptState.Queued)
                != (int)AttemptState.Queued
            )
            {
                continue;
            }

            abandoned.Add(attempt);
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

        try
        {
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
        finally
        {
            // The CAS above is the single ownership transfer, so each hook fires at most once even if
            // the storage release faults.
            foreach (var attempt in abandoned)
            {
                attempt._onAbandonedBeforeExecution?.Invoke();
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

        try
        {
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
        finally
        {
            // Only Claimed→Abandoned and Queued→Abandoned reach here with next == Abandoned; the
            // Running→Completed transition never fires the pre-execution hook.
            if (next is AttemptState.Abandoned)
            {
                _onAbandonedBeforeExecution?.Invoke();
            }
        }
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
