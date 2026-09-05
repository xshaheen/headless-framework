// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedGeneration ?? 1, nameof(expectedGeneration));
        JobIntentFingerprint.RejectOrdinaryMutation(job);
        JobIntentFingerprint.Normalize(job);
        lock (_keyedOperations)
        {
            var current = _FindCurrent(new JobKeyScope(job.Function, job.TenantId), key);
            if (current is not null && expectedGeneration is null)
            {
                var fingerprint = JobIntentFingerprint.Compute(job, current.FingerprintAlgorithm!);
                return Task.FromResult(
                    JobIntentFingerprint.Result(
                        current,
                        string.Equals(fingerprint, current.IntentFingerprint, StringComparison.Ordinal)
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
            if (!_timeJobs.TryAdd(row.Id, barrier))
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
                if (!_timeJobs.TryUpdate(current.Id, historical, current))
                {
                    _timeJobs.TryRemove(new KeyValuePair<Guid, TTimeJob>(row.Id, barrier));
                    return Task.FromResult(
                        JobIntentFingerprint.Result(
                            _FindCurrent(new JobKeyScope(job.Function, job.TenantId), key),
                            JobScheduleDisposition.Conflict
                        )
                    );
                }
            }

            if (!_timeJobs.TryUpdate(row.Id, row, barrier))
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
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedGeneration);
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

                if (_timeJobs.TryUpdate(current.Id, updated, current))
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
        _timeJobs.Values.SingleOrDefault(job =>
            job.IsCurrentGeneration == true
            && string.Equals(job.BusinessKey, key.Value, StringComparison.Ordinal)
            && string.Equals(job.Function, scope.Function, StringComparison.Ordinal)
            && string.Equals(job.TenantId, scope.TenantId, StringComparison.Ordinal)
        );

    private void _RejectKeyedTreeUpdate(TTimeJob candidate)
    {
        JobIntentFingerprint.RejectOrdinaryMutation(candidate);
        if (_timeJobs.TryGetValue(candidate.Id, out var stored))
        {
            JobIntentFingerprint.RejectOrdinaryMutation(stored);
        }

        foreach (var child in candidate.Children)
        {
            _RejectKeyedTreeUpdate(child);
        }
    }
}
