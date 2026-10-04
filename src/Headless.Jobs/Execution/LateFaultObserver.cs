// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Jobs;

/// <summary>
/// Logs the fault of a task the caller has stopped awaiting. Post-commit work that overran its deadline keeps
/// running; without this continuation its failure would surface only as an unobserved task exception.
/// </summary>
internal static class LateFaultObserver
{
    internal static void ObserveLateFault(
        Task task,
        ILogger logger,
        string jobScope,
        Action<ILogger, string, Exception> onFault
    )
    {
        _ = task.ContinueWith(
            static (settled, state) =>
            {
                var (continuationLogger, continuationJobScope, log) = ((
                    ILogger,
                    string,
                    Action<ILogger, string, Exception>
                ))
                    state!;
                log(continuationLogger, continuationJobScope, settled.Exception!);
            },
            (logger, jobScope, onFault),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }
}
