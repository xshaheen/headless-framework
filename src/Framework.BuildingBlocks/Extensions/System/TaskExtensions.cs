using Nito.AsyncEx;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.Threading.Tasks;

public static class TaskExtensions
{
    // Get a single exception if only one has been thrown;
    // Get an AggregateException if more than one exception has been thrown collectively by one or more tasks;
    // Propagate the cancellation status properly (Task.IsCanceled), as something like this would not do that: Task t = Task.WhenAll(...); try { await t; } catch { throw t.Exception; }.
    // See: https://stackoverflow.com/a/62607500 (modified)
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
    /// https://www.meziantou.net/fire-and-forget-a-task-in-dotnet.htm
    /// </summary>
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
                // No need to resume on the original SynchronizationContext
                await task.ConfigureAwait(false);
            }
            catch
            {
                // Nothing to do here
            }
        }
    }
}
