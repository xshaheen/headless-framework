using Framework.Kernel.BuildingBlocks.Helpers.System;

namespace Framework.Queueing;

public sealed class FoundatioQueue<T>(Foundatio.Queues.IQueue<T> foundatio) : Framework.Queueing.IQueue<T>
    where T : class
{
    public string QueueId { get; }

    public AsyncEvent<EnqueuingEventArgs<T>> Enqueuing { get; }

    public AsyncEvent<EnqueuedEventArgs<T>> Enqueued { get; }

    public AsyncEvent<DequeuedEventArgs<T>> Dequeued { get; }

    public AsyncEvent<LockRenewedEventArgs<T>> LockRenewed { get; }

    public AsyncEvent<CompletedEventArgs<T>> Completed { get; }

    public AsyncEvent<AbandonedEventArgs<T>> Abandoned { get; }

    public Task<string> EnqueueAsync(T data, QueueEntryOptions? options = null)
    {
        return foundatio.EnqueueAsync(data, options);
    }

    public Task<IQueueEntry<T>> DequeueAsync(CancellationToken cancellationToken) { }

    public Task<IQueueEntry<T>> DequeueAsync(TimeSpan? timeout = null)
    {
        throw new NotImplementedException();
    }

    public Task RenewLockAsync(IQueueEntry<T> entry)
    {
        throw new NotImplementedException();
    }

    public Task CompleteAsync(IQueueEntry<T> entry)
    {
        throw new NotImplementedException();
    }

    public Task AbandonAsync(IQueueEntry<T> entry)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<T>> GetDeadLetterItemsAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task StartAsync(
        Func<IQueueEntry<T>, CancellationToken, Task> handler,
        bool autoComplete = false,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotImplementedException();
    }

    public Task<QueueStats> GetQueueStatsAsync()
    {
        throw new NotImplementedException();
    }

    public Task DeleteQueueAsync()
    {
        throw new NotImplementedException();
    }

    public void AttachBehavior(IQueueBehavior<T> behavior)
    {
        throw new NotImplementedException();
    }
}
