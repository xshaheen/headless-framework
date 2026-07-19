// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

// ReSharper disable once CheckNamespace
namespace System.Collections.Generic;

public static partial class HeadlessEnumerableExtensions
{
    /// <summary>
    /// Invokes an asynchronous <paramref name="action"/> for each element of <paramref name="source"/> concurrently,
    /// using a degree of parallelism equal to <see cref="Environment.ProcessorCount"/>.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The sequence to iterate.</param>
    /// <param name="action">The asynchronous action to invoke for each element.</param>
    /// <returns>A <see cref="Task"/> that completes when the action has run for every element.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="source"/> or <paramref name="action"/> is <see langword="null"/>.
    /// </exception>
    public static Task ParallelForEachAsync<TSource>(this IEnumerable<TSource> source, Func<TSource, Task> action)
    {
        return source.ParallelForEachAsync(action, CancellationToken.None);
    }

    /// <summary>
    /// Invokes an asynchronous <paramref name="action"/> for each element of <paramref name="source"/> concurrently,
    /// using a degree of parallelism equal to <see cref="Environment.ProcessorCount"/>.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The sequence to iterate.</param>
    /// <param name="action">The asynchronous action to invoke for each element.</param>
    /// <param name="cancellationToken">A token that cancels the parallel operation.</param>
    /// <returns>A <see cref="Task"/> that completes when the action has run for every element.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="source"/> or <paramref name="action"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    public static Task ParallelForEachAsync<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, Task> action,
        CancellationToken cancellationToken
    )
    {
        return source.ParallelForEachAsync(Environment.ProcessorCount, action, cancellationToken);
    }

    /// <summary>
    /// Invokes an asynchronous <paramref name="action"/> for each element of <paramref name="source"/> concurrently,
    /// limiting the number of in-flight invocations to <paramref name="degreeOfParallelism"/>.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The sequence to iterate.</param>
    /// <param name="degreeOfParallelism">The maximum number of concurrent invocations. Must be a positive value or <c>-1</c> for unlimited.</param>
    /// <param name="action">The asynchronous action to invoke for each element.</param>
    /// <returns>A <see cref="Task"/> that completes when the action has run for every element.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="source"/> or <paramref name="action"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="degreeOfParallelism"/> is <c>0</c> or less than <c>-1</c>.
    /// </exception>
    public static Task ParallelForEachAsync<TSource>(
        this IEnumerable<TSource> source,
        int degreeOfParallelism,
        Func<TSource, Task> action
    )
    {
        return source.ParallelForEachAsync(degreeOfParallelism, action, CancellationToken.None);
    }

    /// <summary>
    /// Invokes an asynchronous <paramref name="action"/> for each element of <paramref name="source"/> concurrently,
    /// limiting the number of in-flight invocations to <paramref name="degreeOfParallelism"/>.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The sequence to iterate.</param>
    /// <param name="degreeOfParallelism">The maximum number of concurrent invocations. Must be a positive value or <c>-1</c> for unlimited.</param>
    /// <param name="action">The asynchronous action to invoke for each element.</param>
    /// <param name="cancellationToken">A token that cancels the parallel operation.</param>
    /// <returns>A <see cref="Task"/> that completes when the action has run for every element.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="source"/> or <paramref name="action"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="degreeOfParallelism"/> is <c>0</c> or less than <c>-1</c>.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
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
