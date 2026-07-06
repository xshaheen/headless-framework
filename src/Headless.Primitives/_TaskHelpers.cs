// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace System.Threading.Tasks;

/// <summary>
/// Internal copy of the <c>Headless.Extensions</c> task fault-flattening helper used by <c>AsyncEvent</c>. Duplicated
/// so the dependency direction stays <c>Headless.Extensions → Headless.Primitives</c>. Keep in sync with the original
/// in <c>Headless.Extensions/Threading/TaskExtensions.cs</c> if the logic changes.
/// </summary>
internal static class TaskPrimitiveHelpers
{
    /// <summary>
    /// Get a single exception if only one has been thrown; get an <see cref="AggregateException"/> if more than one has
    /// been thrown collectively; and propagate cancellation status properly.
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
}
