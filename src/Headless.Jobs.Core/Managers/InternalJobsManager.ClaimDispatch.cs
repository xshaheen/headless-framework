// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;

namespace Headless.Jobs.Managers;

internal sealed partial class InternalJobsManager<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private async Task<JobExecutionState[]> _QueueNextTimeJobsAsync(
        TimeJobEntity[] minTimeJobs,
        CancellationToken cancellationToken = default
    )
    {
        var results = new List<JobExecutionState>();

        try
        {
            await foreach (var updatedTimeJob in persistenceProvider.QueueTimeJobsAsync(minTimeJobs, cancellationToken))
            {
                results.Add(_BuildQueuedTimeJobContext(updatedTimeJob));

                await _NotifyBestEffortAsync(
                        () => notificationHubSender.UpdateTimeJobNotifyAsync(updatedTimeJob),
                        updatedTimeJob.Id
                    )
                    .ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            await _ReleaseAbandonedClaimsAsync(results).ConfigureAwait(false);
            throw;
        }

        return [.. results];
    }

    private async Task<JobExecutionState[]> _QueueNextCronJobsAsync(
        (DateTime Key, JobManagerDispatchContext[] Items) minCronJob,
        CancellationToken cancellationToken = default
    )
    {
        var results = new List<JobExecutionState>();

        try
        {
            await foreach (
                var occurrence in persistenceProvider
                    .QueueCronJobOccurrencesAsync(minCronJob, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                results.Add(
                    new JobExecutionState
                    {
                        ParentId = occurrence.CronJobId,
                        FunctionName = occurrence.CronJob.Function,
                        JobId = occurrence.Id,
                        Type = JobType.CronJobOccurrence,
                        Retries = occurrence.CronJob.Retries,
                        RetryCount = occurrence.RetryCount,
                        RetryIntervals = occurrence.CronJob.RetryIntervals,
                        ExecutionTime = occurrence.ExecutionTime,
                    }
                );

                await _NotifyBestEffortAsync(
                        () =>
                            occurrence.CreatedAt == occurrence.UpdatedAt
                                ? notificationHubSender.AddCronOccurrenceAsync(occurrence.CronJobId, occurrence)
                                : notificationHubSender.UpdateCronOccurrenceAsync(occurrence.CronJobId, occurrence),
                        occurrence.Id
                    )
                    .ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            await _ReleaseAbandonedClaimsAsync(results).ConfigureAwait(false);
            throw;
        }

        return [.. results];
    }

    public async Task<JobExecutionState[]> RunTimedOutTickers(CancellationToken cancellationToken = default)
    {
        var results = new List<JobExecutionState>();

        try
        {
            await foreach (
                var timedOutTimeJob in persistenceProvider
                    .QueueTimedOutTimeJobsAsync(cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                results.Add(_BuildQueuedTimeJobContext(timedOutTimeJob));

                await _NotifyBestEffortAsync(
                        () => notificationHubSender.UpdateTimeJobNotifyAsync(timedOutTimeJob),
                        timedOutTimeJob.Id
                    )
                    .ConfigureAwait(false);
            }

            await foreach (
                var timedOutCronJob in persistenceProvider
                    .QueueTimedOutCronJobOccurrencesAsync(cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                var functionContext = new JobExecutionState
                {
                    FunctionName = timedOutCronJob.CronJob.Function,
                    JobId = timedOutCronJob.Id,
                    Type = JobType.CronJobOccurrence,
                    Retries = timedOutCronJob.CronJob.Retries,
                    RetryCount = timedOutCronJob.RetryCount,
                    RetryIntervals = timedOutCronJob.CronJob.RetryIntervals,
                    ParentId = timedOutCronJob.CronJobId,
                    ExecutionTime = timedOutCronJob.ExecutionTime,
                };

                results.Add(functionContext);

                await _NotifyBestEffortAsync(
                        () => notificationHubSender.UpdateCronOccurrenceFromExecutionState<TCronJob>(functionContext),
                        functionContext.JobId
                    )
                    .ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            await _ReleaseAbandonedClaimsAsync(results).ConfigureAwait(false);
            throw;
        }

        return [.. results];
    }

    // Dashboard notifications are pure observability, but they are awaited INSIDE the claim enumeration: letting one
    // propagate would abandon rows the claim strategy already committed as Queued+leased, and no node can re-claim
    // those until the lease lapses (~LeaseDuration of added latency per incident). So a send failure is logged and
    // swallowed. Cancellation is not special-cased: these senders take no token, so an OperationCanceledException
    // from one is a foreign/internal cancellation, and the enumerator's own cancellation checks still stop the loop.
    private async Task _NotifyBestEffortAsync(Func<Task> send, Guid jobId)
    {
        try
        {
            await send().ConfigureAwait(false);
        }
#pragma warning disable ERP022 // Observability-only side effect: logged, not rethrown.
        catch (Exception exception)
        {
            logger.LogClaimNotificationFailed(exception, jobId);
        }
#pragma warning restore ERP022
    }

    // Returns rows this node claimed but will never dispatch because the claim enumeration aborted part-way. The
    // scheduler's catch-all cannot do this for us: those rows were never yielded, so they are in no execution context.
    // CancellationToken.None because the abort is frequently the caller's own cancellation (host shutdown) and the
    // release must still happen — this mirrors the scheduler's shutdown release. Best-effort: a failure here must not
    // replace the original exception (the fallback sweep reclaims the rows once their lease lapses).
    private async Task _ReleaseAbandonedClaimsAsync(List<JobExecutionState> claimed)
    {
        if (claimed.Count == 0)
        {
            return;
        }

        try
        {
            await ReleaseAcquiredResources([.. claimed], CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable ERP022 // Recovery side effect: logged, not rethrown — the original failure must surface.
        catch (Exception exception)
        {
            logger.LogAbandonedClaimReleaseFailed(exception, claimed.Count);
        }
#pragma warning restore ERP022
    }
}
