// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
using System.Runtime.CompilerServices;

namespace System.Collections.Generic;

[PublicAPI]
public static class QueueExtensions
{
    extension<T>(Queue<T> queue)
    {
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

    public static Queue<T> ToQueue<T>(this IEnumerable<T> items)
    {
        var queue = new Queue<T>();

        foreach (var item in items)
        {
            queue.Enqueue(item);
        }

        return queue;
    }
}
