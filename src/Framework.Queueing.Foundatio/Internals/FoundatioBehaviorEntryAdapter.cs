// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Queueing.Internals;

internal sealed class FoundatioBehaviorEntryAdapter<TValue>(IQueueBehavior<TValue> entry)
    : Foundatio.Queues.IQueueBehavior<TValue>
    where TValue : class
{
    public void Attach(Foundatio.Queues.IQueue<TValue> queue)
    {
        entry.Attach(new QueueFoundatioAdapter<TValue>(queue));
    }
}
