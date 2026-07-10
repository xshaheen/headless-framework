// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.JobsThreadPool;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Jobs.BackgroundServices;

internal sealed class JobsFallbackBackgroundService(
    IInternalJobManager internalJobsManager,
    SchedulerOptionsBuilder schedulerOptions,
    JobsExecutionTaskHandler tickerExecutionTaskHandler,
    JobsTaskScheduler jobsTaskScheduler,
    IJobFunctionConcurrencyGate concurrencyGate,
    TimeProvider timeProvider,
    ILogger<JobsFallbackBackgroundService> logger
) : BackgroundService
{
    private int _started;
    private readonly TimeSpan _fallbackJobPeriod = schedulerOptions.FallbackIntervalChecker;

    public override Task StartAsync(CancellationToken ct)
    {
        return Interlocked.CompareExchange(ref _started, 1, 0) != 0 ? Task.CompletedTask : base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // If the scheduler is frozen or disposed (e.g., manual start mode or shutdown),
                // skip queuing fallback work to avoid throwing and stopping the host.
                if (jobsTaskScheduler.IsFrozen || jobsTaskScheduler.IsDisposed)
                {
                    await timeProvider.Delay(_fallbackJobPeriod, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                // #316/U3: reclaim jobs stalled InProgress with a lapsed lease before re-queuing timed-out work, so
                // a Retry row released to Idle here is picked up by RunTimedOutTickers in the same tick. Closes the
                // gap where a job wedged on a still-live node is reclaimed by neither the claim predicate nor the
                // dead-node sweep.
                await internalJobsManager.ReclaimStalledResources(stoppingToken).ConfigureAwait(false);

                var functions = await internalJobsManager.RunTimedOutTickers(stoppingToken).ConfigureAwait(false);

                if (functions.Length != 0)
                {
                    foreach (var function in functions)
                    {
                        if (JobFunctionProvider.JobFunctions.TryGetValue(function.FunctionName, out var tickerItem))
                        {
                            function.CachedDelegate = tickerItem.Delegate;
                            function.CachedPriority = tickerItem.Priority;
                            function.CachedMaxConcurrency = tickerItem.MaxConcurrency;
                        }

                        foreach (var child in function.TimeJobChildren)
                        {
                            if (JobFunctionProvider.JobFunctions.TryGetValue(child.FunctionName, out var childItem))
                            {
                                child.CachedDelegate = childItem.Delegate;
                                child.CachedPriority = childItem.Priority;
                                child.CachedMaxConcurrency = childItem.MaxConcurrency;
                            }

                            foreach (var grandChild in child.TimeJobChildren)
                            {
                                if (
                                    JobFunctionProvider.JobFunctions.TryGetValue(
                                        grandChild.FunctionName,
                                        out var grandChildItem
                                    )
                                )
                                {
                                    grandChild.CachedDelegate = grandChildItem.Delegate;
                                    grandChild.CachedPriority = grandChildItem.Priority;
                                    grandChild.CachedMaxConcurrency = grandChildItem.MaxConcurrency;
                                }
                            }
                        }

                        var semaphore = concurrencyGate.GetSemaphoreOrNull(
                            function.FunctionName,
                            function.CachedMaxConcurrency
                        );

                        try
                        {
                            await jobsTaskScheduler
                                .QueueAsync(
                                    async ct =>
                                    {
                                        if (semaphore != null)
                                        {
                                            await semaphore.WaitAsync(ct).ConfigureAwait(false);
                                        }

                                        try
                                        {
                                            var claimed = await internalJobsManager
                                                .SetTickersInProgress([function], ct)
                                                .ConfigureAwait(false);
                                            if (claimed.Length == 0)
                                            {
                                                return;
                                            }

                                            await tickerExecutionTaskHandler
                                                .ExecuteTaskAsync(claimed[0], isDue: true, cancellationToken: ct)
                                                .ConfigureAwait(false);
                                        }
                                        finally
                                        {
                                            semaphore?.Release();
                                        }
                                    },
                                    function.CachedPriority,
                                    stoppingToken
                                )
                                .ConfigureAwait(false);
                        }
                        catch (InvalidOperationException)
                            when (jobsTaskScheduler.IsFrozen || jobsTaskScheduler.IsDisposed)
                        {
                            // Scheduler is frozen/disposed – ignore and let loop delay
                            break;
                        }
                    }

                    await timeProvider.Delay(TimeSpan.FromMilliseconds(10), stoppingToken).ConfigureAwait(false);
                }
                else
                {
                    await timeProvider.Delay(_fallbackJobPeriod, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host is shutting down – exit gracefully.
                break;
            }
#pragma warning disable ERP022 // Background service must continue running even if individual operations fail.
            catch (Exception exception)
            {
                // Swallow unexpected exceptions so they don't bubble up
                // and stop the host; wait a bit before retrying.
                logger.LogJobsFallbackTickFailed(exception, _fallbackJobPeriod);
                await timeProvider.Delay(_fallbackJobPeriod, stoppingToken).ConfigureAwait(false);
            }
#pragma warning restore ERP022
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _started, 0);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal static partial class JobsFallbackBackgroundServiceLog
{
    [LoggerMessage(
        EventId = 3200,
        Level = LogLevel.Warning,
        Message = "Jobs fallback tick failed; the service will retry after {FallbackPeriod}."
    )]
    public static partial void LogJobsFallbackTickFailed(
        this ILogger logger,
        Exception exception,
        TimeSpan fallbackPeriod
    );
}
