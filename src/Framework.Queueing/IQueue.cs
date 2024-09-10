// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.BuildingBlocks.Helpers.System;

namespace Framework.Queueing;

public interface IQueue<T>
    where T : class
{
    string QueueId { get; }

    IAsyncEvent<EnqueuingEventArgs<T>> Enqueuing { get; }

    IAsyncEvent<EnqueuedEventArgs<T>> Enqueued { get; }

    IAsyncEvent<DequeuedEventArgs<T>> Dequeued { get; }

    IAsyncEvent<LockRenewedEventArgs<T>> LockRenewed { get; }

    IAsyncEvent<CompletedEventArgs<T>> Completed { get; }

    IAsyncEvent<AbandonedEventArgs<T>> Abandoned { get; }

    Task<string> EnqueueAsync(T data, QueueEntryOptions? options = null);

    Task<IQueueEntry<T>> DequeueAsync(CancellationToken cancellationToken);

    Task<IQueueEntry<T>> DequeueAsync(TimeSpan? timeout = null);

    Task RenewLockAsync(IQueueEntry<T> entry);

    Task CompleteAsync(IQueueEntry<T> entry);

    Task AbandonAsync(IQueueEntry<T> entry);

    Task<IEnumerable<T>> GetDeadLetterItemsAsync(CancellationToken cancellationToken = default);

    Task<QueueStats> GetQueueStatsAsync();

    Task DeleteQueueAsync();

    void AttachBehavior(IQueueBehavior<T> behavior);

    /// <summary>Asynchronously dequeues entries in the background.</summary>
    /// <param name="handler">Function called on entry dequeued.</param>
    /// <param name="autoComplete">True to call <see cref="CompleteAsync"/> after the <paramref name="handler"/> is run, defaults to false.</param>
    /// <param name="cancellationToken">The token used to cancel the background worker.</param>
    Task StartAsync(
        Func<IQueueEntry<T>, CancellationToken, Task> handler,
        bool autoComplete = false,
        CancellationToken cancellationToken = default
    );
}
