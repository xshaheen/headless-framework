// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

// ReSharper disable once CheckNamespace
namespace System.Collections.Generic;

public static partial class HeadlessEnumerableExtensions
{
    /// <summary>
    /// Sequentially invokes an asynchronous <paramref name="action"/> for each element of <paramref name="source"/>,
    /// awaiting each invocation before moving to the next element.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The sequence to iterate.</param>
    /// <param name="action">The asynchronous action to invoke for each element.</param>
    /// <param name="cancellationToken">A token checked before each element is processed.</param>
    /// <returns>A <see cref="Task"/> that completes when the action has run for every element.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="source"/> or <paramref name="action"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
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

    /// <summary>
    /// Sequentially invokes an asynchronous <paramref name="action"/> for each element of <paramref name="source"/>,
    /// passing the <paramref name="cancellationToken"/> to the action, and awaiting each invocation before moving to the next element.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The sequence to iterate.</param>
    /// <param name="action">The asynchronous action to invoke for each element, receiving the cancellation token.</param>
    /// <param name="cancellationToken">A token checked before each element is processed and passed to <paramref name="action"/>.</param>
    /// <returns>A <see cref="Task"/> that completes when the action has run for every element.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="source"/> or <paramref name="action"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
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

    /// <summary>
    /// Sequentially invokes an asynchronous <paramref name="action"/> for each element of <paramref name="source"/>,
    /// passing the element's zero-based index, and awaiting each invocation before moving to the next element.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The sequence to iterate.</param>
    /// <param name="action">The asynchronous action to invoke for each element, receiving the element and its zero-based index.</param>
    /// <param name="cancellationToken">A token checked before each element is processed.</param>
    /// <returns>A <see cref="Task"/> that completes when the action has run for every element.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="source"/> or <paramref name="action"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
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

    /// <summary>
    /// Sequentially invokes an asynchronous <paramref name="action"/> for each element of <paramref name="source"/>,
    /// passing the element's zero-based index and the <paramref name="cancellationToken"/>, and awaiting each invocation
    /// before moving to the next element.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
    /// <param name="source">The sequence to iterate.</param>
    /// <param name="action">The asynchronous action to invoke for each element, receiving the element, its zero-based index, and the cancellation token.</param>
    /// <param name="cancellationToken">A token checked before each element is processed and passed to <paramref name="action"/>.</param>
    /// <returns>A <see cref="Task"/> that completes when the action has run for every element.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="source"/> or <paramref name="action"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
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
