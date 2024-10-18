// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.ComponentModel;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Queueing;

public sealed class EnqueuingEventArgs<T> : CancelEventArgs
    where T : class
{
    public required IQueue<T> Queue { get; init; }

    public required T Data { get; init; }

    public required QueueEntryOptions Options { get; init; }
}
