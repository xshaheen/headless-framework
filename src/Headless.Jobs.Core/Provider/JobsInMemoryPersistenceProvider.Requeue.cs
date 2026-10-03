// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;

namespace Headless.Jobs.Provider;

// Operator requeue for the in-memory provider: a Failed time job or cron occurrence returns to Idle under the same
// locks that keyed scheduling and cron materialization hold, so the refusal checks stay true until the write lands.
internal sealed partial class JobsInMemoryPersistenceProvider<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    public Task<JobRequeueOutcome> RequeueTimeJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        // The keyed-operations lock is what keyed scheduling holds while it reads and supersedes a generation, so the
        // current-generation check and the write below cannot interleave with a supersede.
        lock (_keyedOperations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_timeJobs.TryGetValue(jobId, out var job))
            {
                return Task.FromResult(JobRequeueOutcome.NotFound);
            }

            var refusal = JobRequeueRules.RefuseTimeJob(
                job.Status,
                job.ParentId is not null || _GetChildrenIds(jobId).Length != 0,
                job.BusinessKey,
                job.IsCurrentGeneration
            );
            if (refusal is { } refused)
            {
                return Task.FromResult(refused);
            }

            var now = _timeProvider.GetUtcNow();
            var updated = _CloneTicker(job);
            updated.Status = JobStatus.Idle;
            updated.RetryCount = 0;
            updated.ExceptionMessage = null;
            updated.SkippedReason = null;
            updated.OwnerId = null;
            updated.LockedUntil = null;
            updated.ExecutedAt = null;
            updated.ElapsedTime = 0;
            updated.CancelRequested = false;
            updated.ExecutionTime = now.UtcDateTime;
            updated.UpdatedAt = now;

            if (!_TryUpdateTimeJob(jobId, updated, job))
            {
                return Task.FromResult(JobRequeueOutcome.Conflict);
            }

            _SyncReconcileCandidate(updated);
            return Task.FromResult(JobRequeueOutcome.Requeued);
        }
    }

    public Task<JobRequeueOutcome> RequeueCronJobOccurrenceAsync(
        Guid occurrenceId,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_cronOccurrences.TryGetValue(occurrenceId, out var observed))
        {
            return Task.FromResult(JobRequeueOutcome.NotFound);
        }

        if (observed.Status != JobStatus.Failed)
        {
            return Task.FromResult(JobRequeueOutcome.NotFailed);
        }

        // The per-definition lock is the materialization mutex: every new occurrence of the definition is created
        // under it, so the overlap and instant checks below stay true until the write lands.
        lock (_GetCronDefinitionLock(observed.CronJobId))
        {
            if (!_cronOccurrences.TryGetValue(occurrenceId, out var current))
            {
                return Task.FromResult(JobRequeueOutcome.NotFound);
            }

            if (current.Status != JobStatus.Failed)
            {
                return Task.FromResult(JobRequeueOutcome.Conflict);
            }

            // An occurrence whose definition is gone has no overlap rule to check and nothing to run it against, so it
            // is refused the same way the relational store refuses it when the definition row cannot be locked.
            var cronJobId = current.CronJobId;
            if (!_cronJobs.TryGetValue(cronJobId, out var definition))
            {
                return Task.FromResult(JobRequeueOutcome.NotFound);
            }

            if (
                CronOverlapRule.ForbidsOverlap(definition.OnOverlap)
                && _cronOccurrences.Values.Any(x =>
                    x.CronJobId == cronJobId && x.Id != occurrenceId && CronOverlapRule.IsUnfinished(x.Status)
                )
            )
            {
                return Task.FromResult(JobRequeueOutcome.Overlap);
            }

            // The occurrence keeps its instant, and an instant may hold only one live row.
            var executionTime = current.ExecutionTime;
            if (
                _cronOccurrences.Values.Any(x =>
                    x.CronJobId == cronJobId
                    && x.Id != occurrenceId
                    && x.ExecutionTime == executionTime
                    && CronOverlapRule.IsUnfinished(x.Status)
                )
            )
            {
                return Task.FromResult(JobRequeueOutcome.Conflict);
            }

            var now = _timeProvider.GetUtcNow();
            var updated = _CloneCronOccurrence(current);
            updated.Status = JobStatus.Idle;
            updated.RetryCount = 0;
            updated.ExceptionMessage = null;
            updated.SkippedReason = null;
            updated.OwnerId = null;
            updated.LockedUntil = null;
            updated.ExecutedAt = null;
            updated.ElapsedTime = 0;
            updated.UpdatedAt = now;

            return Task.FromResult(
                _cronOccurrences.TryUpdate(occurrenceId, updated, current)
                    ? JobRequeueOutcome.Requeued
                    : JobRequeueOutcome.Conflict
            );
        }
    }
}
