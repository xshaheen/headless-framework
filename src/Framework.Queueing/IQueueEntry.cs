// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Queueing;

public interface IQueueEntry<out T> : IAsyncDisposable
{
    string Id { get; }

    string CorrelationId { get; }

    bool IsCompleted { get; }

    bool IsAbandoned { get; }

    int Attempts { get; }

    T Value { get; }

    IDictionary<string, string> Properties { get; }

    void MarkAbandoned();

    void MarkCompleted();

    Task RenewLockAsync();

    Task AbandonAsync();

    Task CompleteAsync();
}
