using System.Diagnostics;
using System.Runtime.CompilerServices;
using Nito.AsyncEx;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.Threading.Tasks;

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

    #region Synchronous

    /// <summary>
    /// Waits for the task to complete, unwrapping any exceptions.
    /// </summary>
    /// <param name="task">The task. May not be <c>null</c>.</param>
    public static void WaitAndUnwrapException(this Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Waits for the task to complete, unwrapping any exceptions.
    /// </summary>
    /// <param name="task">The task. May not be <c>null</c>.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <exception cref="OperationCanceledException">The <paramref name="cancellationToken"/> was cancelled before the <paramref name="task"/> completed, or the <paramref name="task"/> raised an <see cref="OperationCanceledException"/>.</exception>
    public static void WaitAndUnwrapException(this Task task, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);

        try
        {
            task.Wait(cancellationToken);
        }
        catch (AggregateException ex)
        {
            throw ex.InnerException!.ReThrow();
        }
    }

    /// <summary>
    /// Waits for the task to complete, unwrapping any exceptions.
    /// </summary>
    /// <typeparam name="TResult">The type of the result of the task.</typeparam>
    /// <param name="task">The task. May not be <c>null</c>.</param>
    /// <returns>The result of the task.</returns>
    public static TResult WaitAndUnwrapException<TResult>(this Task<TResult> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Waits for the task to complete, unwrapping any exceptions.
    /// </summary>
    /// <typeparam name="TResult">The type of the result of the task.</typeparam>
    /// <param name="task">The task. May not be <c>null</c>.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>The result of the task.</returns>
    /// <exception cref="OperationCanceledException">The <paramref name="cancellationToken"/> was cancelled before the <paramref name="task"/> completed, or the <paramref name="task"/> raised an <see cref="OperationCanceledException"/>.</exception>
    public static TResult WaitAndUnwrapException<TResult>(this Task<TResult> task, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);

        try
        {
            task.Wait(cancellationToken);
#pragma warning disable VSTHRD104
            return task.Result;
#pragma warning restore VSTHRD104
        }
        catch (AggregateException ex)
        {
            throw ex.InnerException!.ReThrow();
        }
    }

    /// <summary>
    /// Waits for the task to complete, but does not raise task exceptions. The task exception (if any) is unobserved.
    /// </summary>
    /// <param name="task">The task. May not be <c>null</c>.</param>
    public static void WaitWithoutException(this Task task)
    {
        ArgumentNullException.ThrowIfNull(task);

        try
        {
            task.Wait();
        }
#pragma warning disable ERP022
        catch (AggregateException) { }
#pragma warning restore ERP022
    }

    /// <summary>
    /// Waits for the task to complete, but does not raise task exceptions. The task exception (if any) is unobserved.
    /// </summary>
    /// <param name="task">The task. May not be <c>null</c>.</param>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
    /// <exception cref="OperationCanceledException">The <paramref name="cancellationToken"/> was cancelled before the <paramref name="task"/> completed.</exception>
    public static void WaitWithoutException(this Task task, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);

        try
        {
            task.Wait(cancellationToken);
        }
        catch (AggregateException)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable ERP022
        }
#pragma warning restore ERP022
    }

    #endregion
}
