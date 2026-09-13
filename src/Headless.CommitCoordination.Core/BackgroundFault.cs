// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.CommitCoordination;

/// <summary>
/// Observes a task nobody awaits so its fault is logged instead of becoming an unobserved-task exception, without
/// paying for a continuation when the work already completed successfully.
/// </summary>
internal static class BackgroundFault
{
    public static void Observe<TState>(Task task, TState state, Action<TState, AggregateException> onFault)
    {
        if (task.IsCompletedSuccessfully)
        {
            return;
        }

        _ = task.ContinueWith(
            static (t, s) =>
            {
                var (innerState, handler) = ((TState, Action<TState, AggregateException>))s!;
                handler(innerState, t.Exception!);
            },
            (state, onFault),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }
}
