// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

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
        Argument.IsNotNull(source);
        Argument.IsNotNull(action);

        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await action(item).ConfigureAwait(false);
        }
    }

    public static async Task ForEachAsync<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, CancellationToken, Task> action,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(source);
        Argument.IsNotNull(action);

        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await action(item, cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task ForEachAsync<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, int, Task> action,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(source);
        Argument.IsNotNull(action);

        var index = 0;

        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await action(item, index).ConfigureAwait(false);
            index++;
        }
    }

    public static async Task ForEachAsync<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, int, CancellationToken, Task> action,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(source);
        Argument.IsNotNull(action);

        var index = 0;

        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await action(item, index, cancellationToken).ConfigureAwait(false);
            index++;
        }
    }
}
