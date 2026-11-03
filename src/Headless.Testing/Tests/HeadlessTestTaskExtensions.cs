// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Runtime.CompilerServices;
using Headless.Checks;
using Xunit;

namespace System.Threading.Tasks;

/// <summary>
/// Puts a time limit on a test's wait for work another thread, timer, or handler must finish.
/// </summary>
/// <remarks>
/// A test that awaits a signal with no limit (<c>await tcs.Task</c>, <c>.WaitAsync(cancellationToken)</c>) turns one
/// regression into a hang of the whole test module until the runner's own timeout, which names no test and hides the
/// module's other results. <see cref="Bounded(Task, TimeSpan?, string?)"/> fails that one test instead, with a message
/// naming the awaited expression. The limit is a ceiling for a stuck wait, not a timing assertion: it never shortens a
/// wait that passes.
/// </remarks>
[PublicAPI]
public static class HeadlessTestTaskExtensions
{
    /// <summary>The limit <see cref="Bounded(Task, TimeSpan?, string?)"/> applies when no timeout is given.</summary>
    /// <remarks>
    /// Long enough that a continuation starved on a loaded machine still completes, short enough that a stuck wait
    /// fails its test well before the runner's own timeout.
    /// </remarks>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Waits for <paramref name="task"/> for at most <paramref name="timeout"/>, or until the running test is
    /// cancelled.
    /// </summary>
    /// <param name="task">The task to wait for.</param>
    /// <param name="timeout">The limit; <see cref="DefaultTimeout"/> when <see langword="null"/>.</param>
    /// <param name="expression">Filled in by the compiler with the awaited expression, for the failure message.</param>
    /// <returns>A task that completes as <paramref name="task"/> does.</returns>
    /// <exception cref="TimeoutException">The task did not complete within the limit.</exception>
    /// <exception cref="OperationCanceledException">The running test was cancelled first.</exception>
    public static async Task Bounded(
        this Task task,
        TimeSpan? timeout = null,
        [CallerArgumentExpression(nameof(task))] string? expression = null
    )
    {
        var limit = timeout ?? DefaultTimeout;

        try
        {
            await Argument
                .IsNotNull(task)
                .WaitAsync(limit, TestContext.Current.CancellationToken)
                .ConfigureAwait(false);
        }
        // The guard keeps a TimeoutException the awaited task itself threw as that task's own outcome.
        catch (TimeoutException e) when (!task.IsCompleted)
        {
            throw _Timeout(expression, limit, e);
        }
    }

    /// <inheritdoc cref="Bounded(Task, TimeSpan?, string?)"/>
    /// <returns>The result of <paramref name="task"/>.</returns>
    public static async Task<T> Bounded<T>(
        this Task<T> task,
        TimeSpan? timeout = null,
        [CallerArgumentExpression(nameof(task))] string? expression = null
    )
    {
        var limit = timeout ?? DefaultTimeout;

        try
        {
            return await Argument
                .IsNotNull(task)
                .WaitAsync(limit, TestContext.Current.CancellationToken)
                .ConfigureAwait(false);
        }
        // The guard keeps a TimeoutException the awaited task itself threw as that task's own outcome.
        catch (TimeoutException e) when (!task.IsCompleted)
        {
            throw _Timeout(expression, limit, e);
        }
    }

    /// <inheritdoc cref="Bounded(Task, TimeSpan?, string?)"/>
    public static Task Bounded(
        this ValueTask task,
        TimeSpan? timeout = null,
        [CallerArgumentExpression(nameof(task))] string? expression = null
    )
    {
        return task.AsTask().Bounded(timeout, expression);
    }

    /// <inheritdoc cref="Bounded{T}(Task{T}, TimeSpan?, string?)"/>
    public static Task<T> Bounded<T>(
        this ValueTask<T> task,
        TimeSpan? timeout = null,
        [CallerArgumentExpression(nameof(task))] string? expression = null
    )
    {
        return task.AsTask().Bounded(timeout, expression);
    }

    private static TimeoutException _Timeout(string? expression, TimeSpan limit, TimeoutException inner)
    {
        return new TimeoutException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Waited {limit.TotalSeconds:0.###} s for '{expression}', which did not complete."
            ),
            inner
        );
    }
}
