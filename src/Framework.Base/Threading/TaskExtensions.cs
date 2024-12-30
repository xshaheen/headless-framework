// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Nito.AsyncEx;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.Threading.Tasks;

[PublicAPI]
public static class TaskExtensions
{
    /// <summary><a href="https://www.meziantou.net/fire-and-forget-a-task-in-dotnet.htm">Blog post</a></summary>
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

    #region AggregateException

    /// <summary>
    /// Get a single exception if only one has been thrown;
    /// Get an AggregateException if more than one exception has been thrown collectively by one or more tasks;
    /// Propagate the cancellation status properly (Task.IsCanceled), as something like this would not do that:
    /// Task t = Task.WhenAll(...); try { await t; } catch { throw t.Exception; }.
    /// See: <a href="https://stackoverflow.com/a/62607500">Stack Overflow</a>
    /// </summary>
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

    #region AnyContext

    [DebuggerStepThrough]
    public static ConfiguredTaskAwaitable<TResult> AnyContext<TResult>(this Task<TResult> task)
    {
#pragma warning disable VSTHRD003 // Justification: Its intended to be used.
        return task.ConfigureAwait(continueOnCapturedContext: false);
#pragma warning restore VSTHRD003
    }

    [DebuggerStepThrough]
    public static ConfiguredTaskAwaitable AnyContext(this Task task)
    {
#pragma warning disable VSTHRD003 // Justification: Its intended to be used.
        return task.ConfigureAwait(continueOnCapturedContext: false);
#pragma warning restore VSTHRD003
    }

    [DebuggerStepThrough]
    public static ConfiguredValueTaskAwaitable<TResult> AnyContext<TResult>(this ValueTask<TResult> task)
    {
#pragma warning disable VSTHRD003 // Justification: Its intended to be used.
        return task.ConfigureAwait(continueOnCapturedContext: false);
#pragma warning restore VSTHRD003
    }

    [DebuggerStepThrough]
    public static ConfiguredValueTaskAwaitable AnyContext(this ValueTask task)
    {
#pragma warning disable VSTHRD003 // Justification: Its intended to be used.
        return task.ConfigureAwait(continueOnCapturedContext: false);
#pragma warning restore VSTHRD003
    }

    [DebuggerStepThrough]
    public static ConfiguredTaskAwaitable<TResult> AnyContext<TResult>(this AwaitableDisposable<TResult> task)
        where TResult : IDisposable
    {
        return task.ConfigureAwait(continueOnCapturedContext: false);
    }

    [DebuggerStepThrough]
    public static ConfiguredCancelableAsyncEnumerable<TResult> AnyContext<TResult>(this IAsyncEnumerable<TResult> task)
    {
        return task.ConfigureAwait(continueOnCapturedContext: false);
    }

    #endregion
}
