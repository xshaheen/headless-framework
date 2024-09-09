// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Helpers.System;

namespace Framework.Queueing;

public interface IQueue<T>
    where T : class
{
    string QueueId { get; }

    #region Events

    AsyncEvent<EnqueuingEventArgs<T>> Enqueuing { get; }

    AsyncEvent<EnqueuedEventArgs<T>> Enqueued { get; }

    AsyncEvent<DequeuedEventArgs<T>> Dequeued { get; }

    AsyncEvent<LockRenewedEventArgs<T>> LockRenewed { get; }

    AsyncEvent<CompletedEventArgs<T>> Completed { get; }

    AsyncEvent<AbandonedEventArgs<T>> Abandoned { get; }

    #endregion

    #region Enqueue

    Task<string> EnqueueAsync(T data, QueueEntryOptions? options = null);

    #endregion

    #region Dequeue

    Task<IQueueEntry<T>> DequeueAsync(CancellationToken cancellationToken);

    Task<IQueueEntry<T>> DequeueAsync(TimeSpan? timeout = null);

    #endregion

    #region Entry Actions

    Task RenewLockAsync(IQueueEntry<T> entry);

    Task CompleteAsync(IQueueEntry<T> entry);

    Task AbandonAsync(IQueueEntry<T> entry);

    #endregion

    #region DeadLetters

    Task<IEnumerable<T>> GetDeadLetterItemsAsync(CancellationToken cancellationToken = default);

    #endregion

    #region Start

    /// <summary>Asynchronously dequeues entries in the background.</summary>
    /// <param name="handler">Function called on entry dequeued.</param>
    /// <param name="autoComplete">True to call <see cref="CompleteAsync"/> after the <paramref name="handler"/> is run, defaults to false.</param>
    /// <param name="cancellationToken">The token used to cancel the background worker.</param>
    Task StartAsync(
        Func<IQueueEntry<T>, CancellationToken, Task> handler,
        bool autoComplete = false,
        CancellationToken cancellationToken = default
    );

    #endregion

    Task<QueueStats> GetQueueStatsAsync();

    Task DeleteQueueAsync();

    void AttachBehavior(IQueueBehavior<T> behavior);
}
