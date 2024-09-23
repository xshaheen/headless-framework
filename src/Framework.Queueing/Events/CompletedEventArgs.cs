// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

// ReSharper disable once CheckNamespace
namespace Framework.Queueing;

public sealed class CompletedEventArgs<T> : EventArgs
    where T : class
{
    public required IQueue<T> Queue { get; init; }

    public required IQueueEntry<T> Entry { get; init; }
}
