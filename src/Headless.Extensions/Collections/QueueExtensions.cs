// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
using System.Runtime.CompilerServices;

namespace System.Collections.Generic;

[PublicAPI]
public static class QueueExtensions
{
    extension<T>(Queue<T> queue)
    {
        /// <summary>Adds the elements of the specified span to the end of the <see cref="Queue{T}"/>.</summary>
        /// <param name="items">The elements to enqueue.</param>
        [OverloadResolutionPriority(1)]
        public void EnqueueRange(params ReadOnlySpan<T> items)
        {
            foreach (var item in items)
            {
                queue.Enqueue(item);
            }
        }

        /// <summary>Adds the elements of the specified list to the end of the <see cref="Queue{T}"/>.</summary>
        /// <param name="items">The elements to enqueue.</param>
        public void EnqueueRange(List<T> items)
        {
            queue.EnqueueRange(items.AsReadOnlySpan());
        }

        /// <summary>Adds the elements of the specified sequence to the end of the <see cref="Queue{T}"/>.</summary>
        /// <param name="items">The elements to enqueue.</param>
        public void EnqueueRange(IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                queue.Enqueue(item);
            }
        }
    }

    /// <summary>Creates a <see cref="Queue{T}"/> from a sequence, enqueuing the elements in iteration order.</summary>
    /// <typeparam name="T">The type of the elements of <paramref name="items"/>.</typeparam>
    /// <param name="items">The sequence whose elements are enqueued.</param>
    /// <returns>A <see cref="Queue{T}"/> that contains the elements of <paramref name="items"/>.</returns>
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
