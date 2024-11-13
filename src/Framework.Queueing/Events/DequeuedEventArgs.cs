// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Queueing;

public sealed class DequeuedEventArgs<T> : EventArgs
    where T : class
{
    public required IQueue<T> Queue { get; init; }

    public required IQueueEntry<T> Entry { get; init; }
}
