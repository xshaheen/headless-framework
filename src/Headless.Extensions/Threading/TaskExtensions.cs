// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System.Threading.Tasks;

/// <summary>Extensions for fire-and-forget execution, exception aggregation, result probing, and cancellation of <see cref="Task"/> instances.</summary>
[PublicAPI]
public static class HeadlessTaskExtensions
{
    #region Forget

    /// <summary>
    /// Observes and discards a task so it runs fire-and-forget without surfacing faults to the caller; any exception
    /// the task produces is caught and ignored to avoid unobserved-task escalation.
    /// </summary>
    /// <param name="task">The task to run without awaiting its result.</param>
    /// <seealso href="https://www.meziantou.net/fire-and-forget-a-task-in-dotnet.htm">Fire and forget a task in .NET</seealso>
    public static void Forget(this Task task)
    {
        // Only care about tasks that may fault or are faulted,
        // so fast-path for SuccessfullyCompleted and Canceled tasks
        if (!task.IsCompleted || task.IsFaulted)
        {
            _ = forgetAwaited(task);
        }

        return;

        static async Task forgetAwaited(Task task)
        {
            try
            {
#pragma warning disable VSTHRD003 // Justification: Its intended to be used.
                // No need to resume on the original SynchronizationContext
                await task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch
            {
                // Nothing to do here
#pragma warning disable ERP022 // Justification: Its intended to be used.
            }
#pragma warning restore ERP022
        }
    }

    #endregion

    #region AggregateException

    /// <summary>
    /// Get a single exception if only one has been thrown;
    /// Get an AggregateException if more than one exception has been thrown collectively by one or more tasks;
    /// Propagate the cancellation status properly (Task.IsCanceled), as something like this would not do that:
    /// Task t = Task.WhenAll(...); try { await t; } catch { throw t.Exception; }.
    /// See: <a href="https://stackoverflow.com/a/62607500">Stack Overflow</a>
    /// </summary>
    /// <param name="this">The task whose faults should be flattened into a single aggregated exception when multiple are present.</param>
    /// <returns>A task that mirrors <paramref name="this"/> but surfaces a flattened <see cref="AggregateException"/> when more than one fault occurred.</returns>
    public static Task WithAggregatedExceptions(this Task @this)
    {
        return @this
            .ContinueWith(
                continuationFunction: task =>
                    task.IsFaulted
                    && (task.Exception.InnerExceptions.Count > 1 || task.Exception.InnerException is AggregateException)
                        ? Task.FromException(task.Exception.Flatten())
                        : task,
                cancellationToken: CancellationToken.None,
                continuationOptions: TaskContinuationOptions.NotOnCanceled
                    | TaskContinuationOptions.ExecuteSynchronously,
                scheduler: TaskScheduler.Default
            )
            .Unwrap();
    }

    /// <summary>
    /// Get a single exception if only one has been thrown;
    /// Get an AggregateException if more than one exception has been thrown collectively by one or more tasks;
    /// Propagate the cancellation status properly (Task.IsCanceled), as something like this would not do that:
    /// Task t = Task.WhenAll(...); try { await t; } catch { throw t.Exception; }.
    /// See: <a href="https://stackoverflow.com/a/62607500">Stack Overflow</a>
    /// </summary>
    /// <typeparam name="T">The result type produced by the task.</typeparam>
    /// <param name="this">The task whose faults should be flattened into a single aggregated exception when multiple are present.</param>
    /// <returns>A task that mirrors <paramref name="this"/> but surfaces a flattened <see cref="AggregateException"/> when more than one fault occurred.</returns>
    public static Task<T> WithAggregatedExceptions<T>(this Task<T> @this)
    {
        return @this
            .ContinueWith<Task<T>>(
                continuationFunction: task =>
                    task.IsFaulted
                    && (task.Exception.InnerExceptions.Count > 1 || task.Exception.InnerException is AggregateException)
                        ? Task.FromException<T>(task.Exception.Flatten())
                        : task,
                cancellationToken: CancellationToken.None,
                continuationOptions: TaskContinuationOptions.NotOnCanceled
                    | TaskContinuationOptions.ExecuteSynchronously,
                scheduler: TaskScheduler.Default
            )
            .Unwrap();
    }

    #endregion

    #region Get Result

    /// <summary>
    /// Gets the result of a <see cref="Task{TResult}"/> if available, or <see langword="default"/> otherwise.
    /// </summary>
    /// <typeparam name="T">The type of <see cref="Task{TResult}"/> to get the result for.</typeparam>
    /// <param name="task">The input <see cref="Task{TResult}"/> instance to get the result for.</param>
    /// <returns>The result of <paramref name="task"/> if completed successfully, or <see langword="default"/> otherwise.</returns>
    /// <remarks>This method does not block if <paramref name="task"/> has not completed yet.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? GetResultOrDefault<T>(this Task<T?> task)
    {
#pragma warning disable VSTHRD104 // Justification: Its intended to be used.
        return task.Status == TaskStatus.RanToCompletion ? task.Result : default;
#pragma warning restore VSTHRD104
    }

    // See: https://github.com/CommunityToolkit/dotnet/blob/main/src/CommunityToolkit.Common/Extensions/TaskExtensions.cs

    /// <summary>
    /// Gets the result of a <see cref="Task"/> if available, or <see langword="null"/> otherwise.
    /// </summary>
    /// <param name="task">The input <see cref="Task"/> instance to get the result for.</param>
    /// <returns>The result of <paramref name="task"/> if completed successfully, or <see langword="default"/> otherwise.</returns>
    /// <remarks>
    /// This method does not block if <paramref name="task"/> has not completed yet. Furthermore, it is not generic
    /// and uses reflection to access the <see cref="Task{TResult}.Result"/> property and boxes the result if it's
    /// a value type, which adds overhead. It should only be used when using generics is not possible.
    /// </remarks>
    [RequiresUnreferencedCode(
        "This method uses reflection to try to access the Task<T>.Result property of the input Task instance."
    )]
    public static object? GetResultOrDefault(this Task task)
    {
        // Check if the instance is a completed Task
        if (task.Status is not TaskStatus.RanToCompletion)
        {
            return null;
        }

        // We need an explicit check to ensure the input task is not the cached
        // Task.CompletedTask instance, because that can internally be stored as
        // a Task<T> for some given T (e.g. on .NET 6 it's VoidTaskResult), which
        // would cause the following code to return that result instead of null.
        if (task == Task.CompletedTask)
        {
            return null;
        }

        // Try to get the Task<T>.Result property. This method would've
        // been called anyway after the type checks, but using that to
        // validate the input type saves some additional reflection calls.
        // Furthermore, doing this also makes the method flexible enough to
        // case whether the input Task<T> is actually an instance of some
        // runtime-specific type that inherits from Task<T>.
#pragma warning disable REFL009
        var propertyInfo = task.GetType().GetProperty("Result");
#pragma warning restore REFL009

        // Return the result, if possible
        return propertyInfo?.GetValue(task);
    }

    #endregion

    #region WithCancellation

#pragma warning disable VSTHRD003
    /// <summary>
    /// Wraps a task with one that will complete as cancelled based on a cancellation token,
    /// allowing someone to await a task but be able to break out early by cancelling the token.
    /// </summary>
    /// <typeparam name="T">The type of value returned by the task.</typeparam>
    /// <param name="task">The task to wrap.</param>
    /// <param name="cancellationToken">The token that can be canceled to break out of the await.</param>
    /// <returns>The wrapping task.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="task"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown by the returned task when <paramref name="cancellationToken"/> is cancelled before <paramref name="task"/> completes.</exception>
    public static Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
    {
        _ = Argument.IsNotNull(task);

        if (!cancellationToken.CanBeCanceled || task.IsCompleted)
        {
            return task;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        return _WithCancellationSlow(task, cancellationToken);
    }

    /// <summary>
    /// Wraps a task with one that will complete as cancelled based on a cancellation token,
    /// allowing someone to await a task but be able to break out early by cancelling the token.
    /// </summary>
    /// <param name="task">The task to wrap.</param>
    /// <param name="cancellationToken">The token that can be canceled to break out of the await.</param>
    /// <returns>The wrapping task.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="task"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Thrown by the returned task when <paramref name="cancellationToken"/> is cancelled before <paramref name="task"/> completes.</exception>
    public static Task WithCancellation(this Task task, CancellationToken cancellationToken)
    {
        _ = Argument.IsNotNull(task);

        if (!cancellationToken.CanBeCanceled || task.IsCompleted)
        {
            return task;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        return task._WithCancellationSlow(continueOnCapturedContext: false, cancellationToken: cancellationToken);
    }

    private static async Task<T> _WithCancellationSlow<T>(Task<T> task, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();

        await using (cancellationToken.Register(s => ((TaskCompletionSource<bool>)s!).TrySetResult(true), tcs))
        {
            if (task != await Task.WhenAny(task, tcs.Task).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        // Rethrow any fault/cancellation exception, even if we awaited above.
        // But if we skipped the above if branched, this will actually yield
        // on an incompleted task.
        return await task.ConfigureAwait(false);
    }

    /// <summary>
    /// Wraps a task with one that will complete as cancelled based on a cancellation token,
    /// allowing someone to await a task but be able to break out early by cancelling the token.
    /// </summary>
    /// <param name="task">The task to wrap.</param>
    /// <param name="continueOnCapturedContext">A value indicating whether *internal* continuations required to respond to cancellation should run on the current <see cref="SynchronizationContext"/>.</param>
    /// <param name="cancellationToken">The token that can be canceled to break out of to await.</param>
    /// <returns>The wrapping task.</returns>
    private static async Task _WithCancellationSlow(
        this Task task,
        bool continueOnCapturedContext,
        CancellationToken cancellationToken
    )
    {
        var tcs = new TaskCompletionSource<bool>();

        await using (cancellationToken.Register(s => ((TaskCompletionSource<bool>)s!).TrySetResult(true), tcs))
        {
            if (task != await Task.WhenAny(task, tcs.Task).ConfigureAwait(continueOnCapturedContext))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        // Rethrow any fault/cancellation exception, even if we awaited above.
        // But if we skipped the above if branched, this will actually yield
        // on an incompleted task.
        await task.ConfigureAwait(continueOnCapturedContext);
    }
#pragma warning restore VSTHRD003

    #endregion

    #region Run Delayed

    extension(Task)
    {
        /// <summary>Schedules <paramref name="action"/> to run on the thread pool after waiting for <paramref name="delay"/>.</summary>
        /// <param name="delay">The time to wait before invoking <paramref name="action"/>.</param>
        /// <param name="action">The asynchronous work to run after the delay, passed the <paramref name="cancellationToken"/>.</param>
        /// <param name="timeProvider">The time provider used to schedule the delay; defaults to <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
        /// <param name="cancellationToken">A token that cancels both the delay and the scheduled work.</param>
        /// <returns>A task that completes once <paramref name="action"/> has finished running after the delay.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="delay"/> is not positive.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="action"/> is <see langword="null"/>.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled before or during the delay.</exception>
        public static Task DelayedAsync(
            TimeSpan delay,
            Func<CancellationToken, Task> action,
            TimeProvider? timeProvider = null,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsPositive(delay);
            Argument.IsNotNull(action);

            timeProvider ??= TimeProvider.System;

            cancellationToken.ThrowIfCancellationRequested();

            return Task.Run(
                async () =>
                {
                    if (delay.Ticks > 0)
                    {
                        await timeProvider.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    await action(cancellationToken).ConfigureAwait(false);
                },
                cancellationToken
            );
        }
    }

    #endregion
}
