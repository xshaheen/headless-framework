// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Queueing.Internals;

internal sealed class FoundatioQueueEntryAdapter<TValue>(IQueueEntry<TValue> entry)
    : Foundatio.Queues.IQueueEntry<TValue>
    where TValue : class
{
    public string Id { get; } = entry.Id;

    public string CorrelationId { get; } = entry.CorrelationId;

    public Type EntryType { get; } = typeof(TValue);

    public bool IsCompleted { get; } = entry.IsCompleted;

    public bool IsAbandoned { get; } = entry.IsAbandoned;

    public int Attempts { get; } = entry.Attempts;

    public TValue Value { get; } = entry.Value;

    public IDictionary<string, string> Properties { get; } = entry.Properties;

    public object GetValue() => Value;

    public void MarkAbandoned() => entry.MarkAbandoned();

    public void MarkCompleted() => entry.MarkCompleted();

    public ValueTask DisposeAsync() => entry.DisposeAsync();

    public Task RenewLockAsync() => entry.RenewLockAsync();

    public Task AbandonAsync() => entry.AbandonAsync();

    public Task CompleteAsync() => entry.CompleteAsync();
}
