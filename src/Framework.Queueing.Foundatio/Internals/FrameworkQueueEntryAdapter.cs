namespace Framework.Queueing.Internals;

internal sealed class FrameworkQueueEntryAdapter<TValue>(Foundatio.Queues.IQueueEntry<TValue> entry)
    : IQueueEntry<TValue>
    where TValue : class
{
    public string Id { get; } = entry.Id;

    public string CorrelationId { get; } = entry.CorrelationId;

    public bool IsCompleted { get; } = entry.IsCompleted;

    public bool IsAbandoned { get; } = entry.IsAbandoned;

    public int Attempts { get; } = entry.Attempts;

    public TValue Value { get; } = entry.Value;

    public IDictionary<string, string> Properties { get; } = entry.Properties;

    public void MarkAbandoned() => entry.MarkAbandoned();

    public void MarkCompleted() => entry.MarkCompleted();

    public ValueTask DisposeAsync() => entry.DisposeAsync();

    public Task RenewLockAsync() => entry.RenewLockAsync();

    public Task AbandonAsync() => entry.AbandonAsync();

    public Task CompleteAsync() => entry.CompleteAsync();
}
