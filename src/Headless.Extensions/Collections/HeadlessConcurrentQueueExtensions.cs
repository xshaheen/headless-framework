// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System.Collections.Generic;

[PublicAPI]
public static class HeadlessConcurrentQueueExtensions
{
    extension<T>(ConcurrentQueue<T> queue)
    {
        /// <summary>Adds the elements of the specified span to the end of the <see cref="ConcurrentQueue{T}"/>.</summary>
        /// <param name="items">The elements to enqueue.</param>
        [OverloadResolutionPriority(1)]
        public void EnqueueRange(params ReadOnlySpan<T> items)
        {
            foreach (var item in items)
            {
                queue.Enqueue(item);
            }
        }

        /// <summary>Adds the elements of the specified list to the end of the <see cref="ConcurrentQueue{T}"/>.</summary>
        /// <param name="items">The elements to enqueue.</param>
        public void EnqueueRange(List<T> items)
        {
            queue.EnqueueRange(items.AsReadOnlySpan());
        }

        /// <summary>Adds the elements of the specified sequence to the end of the <see cref="ConcurrentQueue{T}"/>.</summary>
        /// <param name="items">The elements to enqueue.</param>
        public void EnqueueRange(IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                queue.Enqueue(item);
            }
        }
    }
}
