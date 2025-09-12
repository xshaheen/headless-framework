// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Nito.AsyncEx;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.Threading.Tasks;

[PublicAPI]
public static class TaskExtensions
{
    #region Forget

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

    #endregion

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
}
