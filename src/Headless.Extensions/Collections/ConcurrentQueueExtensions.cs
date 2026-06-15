// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System.Collections.Generic;

[PublicAPI]
public static class ConcurrentQueueExtensions
{
    extension<T>(ConcurrentQueue<T> queue)
    {
        public void Clear()
        {
            while (queue.TryDequeue(out _)) { }
        }

        [OverloadResolutionPriority(1)]
        public void EnqueueRange(params ReadOnlySpan<T> items)
        {
            foreach (var item in items)
            {
                queue.Enqueue(item);
            }
        }

        public void EnqueueRange(List<T> items)
        {
            queue.EnqueueRange(items.AsReadOnlySpan());
        }

        public void EnqueueRange(IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                queue.Enqueue(item);
            }
        }
    }
}
