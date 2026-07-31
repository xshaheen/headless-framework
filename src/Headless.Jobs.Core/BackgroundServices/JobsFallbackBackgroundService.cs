// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.JobsThreadPool;
using Headless.Jobs.Models;
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
    JobFunctionRegistry functionRegistry,
    TimeProvider timeProvider,
    IJobsOwnerIdentity ownerIdentity,
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
        // Fail-stop (R9), mirroring the main scheduler loop: the reclaim sweep applies cluster-wide terminal
        // transitions (MarkFailed/Skip), so a node that lost coordination membership must stop sweeping other
        // live nodes' rows — under StopMembershipOnly nothing else would stop this loop. On the in-memory path
        // the token is None and never fires.
        using var membershipLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            ownerIdentity.MembershipLostToken
        );
        var loopToken = membershipLinkedCts.Token;

        while (!loopToken.IsCancellationRequested)
        {
            try
            {
                // If the scheduler is frozen or disposed (e.g., manual start mode or shutdown),
                // skip queuing fallback work to avoid throwing and stopping the host.
                if (jobsTaskScheduler.IsFrozen || jobsTaskScheduler.IsDisposed)
                {
                    await timeProvider.Delay(_fallbackJobPeriod, loopToken).ConfigureAwait(false);
                    continue;
                }

                // #316/U3: reclaim jobs stalled InProgress with a lapsed lease before re-queuing timed-out work, so
                // a Retry row released to Idle here is picked up by RunTimedOutTickers in the same tick. Closes the
                // gap where a job wedged on a still-live node is reclaimed by neither the claim predicate nor the
                // dead-node sweep.
                await internalJobsManager.ReclaimStalledResources(loopToken).ConfigureAwait(false);

                var functions = await internalJobsManager.RunTimedOutTickers(loopToken).ConfigureAwait(false);

                if (functions.Length != 0)
                {
                    foreach (var function in functions)
                    {
                        // U3: attach cached delegates to the whole hydrated tree, not just the grandchild level, so a
                        // chain deeper than three levels also executes its tail on the timed-out fallback path.
                        // Runs before the dispatch sort below because it also stamps CachedPriority.
                        JobsExecutionContext.CacheFunctionReferences(function, functionRegistry);
                    }

                    foreach (var function in functions.OrderBy(x => x.CachedPriority.DispatchRank()))
                    {
                        var semaphore = concurrencyGate.GetSemaphoreOrNull(
                            function.FunctionName,
                            function.CachedMaxConcurrency
                        );

                        try
                        {
                            await jobsTaskScheduler
                                .QueueAsync(
                                    JobsAdmissionWorkItem.Create(
                                        internalJobsManager,
                                        tickerExecutionTaskHandler,
                                        logger,
                                        semaphore,
                                        function,
                                        isDue: true
                                    ),
                                    function.CachedPriority,
                                    loopToken
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

                    await timeProvider.Delay(TimeSpan.FromMilliseconds(10), loopToken).ConfigureAwait(false);
                }
                else
                {
                    await timeProvider.Delay(_fallbackJobPeriod, loopToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (loopToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable ERP022 // Background service must continue running even if individual operations fail.
            catch (Exception exception)
            {
                // Swallow unexpected exceptions so they don't bubble up
                // and stop the host; wait a bit before retrying.
                logger.LogJobsFallbackTickFailed(exception, _fallbackJobPeriod);
                await timeProvider.Delay(_fallbackJobPeriod, loopToken).ConfigureAwait(false);
            }
#pragma warning restore ERP022
        }

        if (ownerIdentity.MembershipLostToken.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            logger.LogJobsFallbackStoppedOnMembershipLoss();
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

    [LoggerMessage(
        EventId = 3201,
        Level = LogLevel.Warning,
        Message = "Jobs fallback service stopped because local coordination membership was lost; "
            + "reclaim sweeps and timed-out re-dispatch no longer run on this node."
    )]
    public static partial void LogJobsFallbackStoppedOnMembershipLoss(this ILogger logger);
}
