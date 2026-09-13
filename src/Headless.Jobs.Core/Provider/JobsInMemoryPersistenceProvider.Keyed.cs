// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;

namespace Headless.Jobs.Provider;

internal sealed partial class JobsInMemoryPersistenceProvider<TTimeJob, TCronJob>
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    private readonly Lock _keyedOperations = new();

    public Task<JobScheduleResult> ScheduleKeyedTimeJobAsync(
        JobKey key,
        TTimeJob job,
        long? expectedGeneration = null,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(job);
        JobAtomicity.RejectDirect([job]);
        Argument.IsNotNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsPositive(expectedGeneration);
        JobIntentFingerprint.RejectOrdinaryMutation(job);
        JobIntentFingerprint.Normalize(job);
        lock (_keyedOperations)
        {
            var current = _FindCurrent(new JobKeyScope(job.Function, job.TenantId), key);
            if (current is not null && expectedGeneration is null)
            {
                return Task.FromResult(
                    JobIntentFingerprint.Result(
                        current,
                        JobIntentFingerprint.Matches(job, current)
                            ? JobScheduleDisposition.Existing
                            : JobScheduleDisposition.Conflict
                    )
                );
            }

            if (expectedGeneration is not null)
            {
                if (current is null)
                {
                    return Task.FromResult(
                        JobIntentFingerprint.Result<TTimeJob>(job: null, JobScheduleDisposition.NotFound)
                    );
                }

                if (current.Generation != expectedGeneration)
                {
                    return Task.FromResult(
                        JobIntentFingerprint.Result(current, JobScheduleDisposition.StaleGeneration)
                    );
                }

                if (
                    current.Status != JobStatus.Idle
                    || current.OwnerId is not null
                    || current.LockedUntil is not null
                    || current.CancelRequested
                )
                {
                    return Task.FromResult(JobIntentFingerprint.Result(current, JobScheduleDisposition.Conflict));
                }
            }

            var row = _CloneTicker(job);
            row.Id = row.Id == Guid.Empty ? _guidGenerator.Create() : row.Id;
            if (_timeJobs.ContainsKey(row.Id))
            {
                throw new InvalidOperationException("The candidate run ID already exists.");
            }
            if (_childrenIndex.TryGetValue(row.Id, out var children) && !children.IsEmpty)
            {
                throw new NotSupportedException(
                    "A keyed run cannot adopt existing children. Keyed JobChain scheduling is unsupported."
                );
            }

            row.BusinessKey = key.Value;
            row.IntentFingerprint = JobIntentFingerprint.Compute(row, JobIntentFingerprint.Algorithm);
            row.FingerprintAlgorithm = JobIntentFingerprint.Algorithm;
            row.Generation = checked((current?.Generation ?? 0) + 1);
            row.IsCurrentGeneration = true;
            row.Status = JobStatus.Idle;
            row.OwnerId = null;
            row.LockedUntil = null;
            row.CreatedAt = row.UpdatedAt = _timeProvider.GetUtcNow();
            var barrier = _CloneTicker(row);
            barrier.Status = JobStatus.InProgress;
            barrier.LockedUntil = _timeProvider.GetUtcNow().UtcDateTime.Add(_PublicationBarrierLease);
            barrier.IsCurrentGeneration = false;
            if (!_TryAddTimeJob(row.Id, barrier))
            {
                throw new InvalidOperationException("The candidate run ID collided with another insert.");
            }

            if (current is not null)
            {
                var historical = _CloneTicker(current);
                historical.IsCurrentGeneration = false;
                historical.Status = JobStatus.Skipped;
                historical.SkippedReason = "Superseded by a newer keyed generation.";
                historical.ExecutedAt = historical.UpdatedAt = row.CreatedAt;
                // Claims use the same exact-instance CAS. A winning claim prevents replacement.
                if (!_TryUpdateTimeJob(current.Id, historical, current))
                {
                    _TryRemoveTimeJob(new KeyValuePair<Guid, TTimeJob>(row.Id, barrier));
                    return Task.FromResult(
                        JobIntentFingerprint.Result(
                            _FindCurrent(new JobKeyScope(job.Function, job.TenantId), key),
                            JobScheduleDisposition.Conflict
                        )
                    );
                }
            }

            if (!_TryUpdateTimeJob(row.Id, row, barrier))
            {
                throw new InvalidOperationException(
                    "The provisional keyed row was unexpectedly modified before publication."
                );
            }

            return Task.FromResult(
                JobIntentFingerprint.Result(
                    row,
                    current is null ? JobScheduleDisposition.Created : JobScheduleDisposition.Replaced
                )
            );
        }
    }

    public Task<JobScheduleResult> CancelKeyedTimeJobAsync(
        JobKeyScope scope,
        JobKey key,
        long expectedGeneration,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(scope);
        Argument.IsNotNull(key);
        Argument.IsPositive(expectedGeneration);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_keyedOperations)
        {
            while (true)
            {
                var current = _FindCurrent(scope, key);
                if (current is null)
                {
                    return Task.FromResult(
                        JobIntentFingerprint.Result<TTimeJob>(job: null, JobScheduleDisposition.NotFound)
                    );
                }

                if (current.Generation != expectedGeneration)
                {
                    return Task.FromResult(
                        JobIntentFingerprint.Result(current, JobScheduleDisposition.StaleGeneration)
                    );
                }

                if (current.Status is not (JobStatus.Idle or JobStatus.Queued or JobStatus.InProgress))
                {
                    return Task.FromResult(JobIntentFingerprint.Result(current, JobScheduleDisposition.Terminal));
                }

                var updated = _CloneTicker(current);
                updated.CancelRequested = true;
                updated.UpdatedAt = _timeProvider.GetUtcNow();
                var pending =
                    current.Status == JobStatus.Idle && current.OwnerId is null && current.LockedUntil is null;
                if (pending)
                {
                    updated.Status = JobStatus.Cancelled;
                    updated.ExecutedAt = updated.UpdatedAt;
                }

                if (_TryUpdateTimeJob(current.Id, updated, current))
                {
                    return Task.FromResult(
                        JobIntentFingerprint.Result(
                            updated,
                            pending ? JobScheduleDisposition.Cancelled : JobScheduleDisposition.CancellationRequested
                        )
                    );
                }
            }
        }
    }

    private TTimeJob? _FindCurrent(JobKeyScope scope, JobKey key) =>
        _currentTimeJobIds.TryGetValue((scope.TenantId, scope.Function, key.Value), out var id) ? _timeJobs[id] : null;

    private void _RejectKeyedParent(Guid? parentId)
    {
        if (parentId is { } id && _timeJobs.TryGetValue(id, out var parent) && parent.BusinessKey is not null)
        {
            throw new InvalidOperationException(
                "An ordinary job cannot attach to a retained keyed parent. Keyed JobChain scheduling is unsupported."
            );
        }
    }

    private void _RejectKeyedTreeUpdates(IEnumerable<TTimeJob> candidates)
    {
        var pending = new Stack<TTimeJob>(candidates);
        var visited = new HashSet<TTimeJob>(ReferenceEqualityComparer.Instance);
        while (pending.TryPop(out var candidate))
        {
            if (!visited.Add(candidate))
            {
                continue;
            }
            JobIntentFingerprint.RejectOrdinaryMetadata(candidate);
            _RejectKeyedParent(candidate.ParentId);
            if (_timeJobs.TryGetValue(candidate.Id, out var stored))
            {
                pending.Push(stored);
            }
            foreach (var child in candidate.Children)
            {
                _RejectKeyedParent(candidate.Id);
                pending.Push(child);
            }
        }
    }
}
