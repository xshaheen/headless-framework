// Copyright (c) Mahmoud Shaheen. All rights reserved.

// ReSharper disable once CheckNamespace
namespace System.Collections.Generic;

public static partial class EnumerableExtensions
{
    public static async Task ForEachAsync<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, Task> action,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(action);

        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await action(item).AnyContext();
        }
    }

    public static async Task ForEachAsync<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, CancellationToken, Task> action,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(action);

        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await action(item, cancellationToken).AnyContext();
        }
    }

    public static async Task ForEachAsync<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, int, Task> action,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(action);

        var index = 0;

        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await action(item, index).AnyContext();
            index++;
        }
    }

    public static async Task ForEachAsync<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, int, CancellationToken, Task> action,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(action);

        var index = 0;

        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await action(item, index, cancellationToken).AnyContext();
            index++;
        }
    }
}
