// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

// ReSharper disable once CheckNamespace
namespace System.Collections.Generic;

public static partial class EnumerableExtensions
{
    public static Task ParallelForEachAsync<TSource>(this IEnumerable<TSource> source, Func<TSource, Task> action)
    {
        return source.ParallelForEachAsync(action, CancellationToken.None);
    }

    public static Task ParallelForEachAsync<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, Task> action,
        CancellationToken cancellationToken
    )
    {
        return source.ParallelForEachAsync(Environment.ProcessorCount, action, cancellationToken);
    }

    public static Task ParallelForEachAsync<TSource>(
        this IEnumerable<TSource> source,
        int degreeOfParallelism,
        Func<TSource, Task> action
    )
    {
        return source.ParallelForEachAsync(degreeOfParallelism, action, CancellationToken.None);
    }

    public static Task ParallelForEachAsync<TSource>(
        this IEnumerable<TSource> source,
        int degreeOfParallelism,
        Func<TSource, Task> action,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(source);
        Argument.IsNotNull(action);

        return Parallel.ForEachAsync(
            source,
            new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism, CancellationToken = cancellationToken },
            (item, _) => new ValueTask(action(item))
        );
    }
}
