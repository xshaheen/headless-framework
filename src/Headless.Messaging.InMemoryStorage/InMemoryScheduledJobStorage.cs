// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Headless.Messaging.InMemoryStorage;

internal sealed class InMemoryScheduledJobStorage(TimeProvider timeProvider) : IScheduledJobStorage, IDisposable
{
    private readonly ConcurrentDictionary<Guid, ScheduledJob> _jobs = new();
    private readonly ConcurrentDictionary<Guid, JobExecution> _executions = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<IReadOnlyList<ScheduledJob>> AcquireDueJobsAsync(
        int batchSize,
        string lockHolder,
        CancellationToken cancellationToken = default
    )
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = timeProvider.GetUtcNow();
            var dueJobs = _jobs
                .Values.Where(j =>
                    j.Status == ScheduledJobStatus.Pending
                    && j.IsEnabled
                    && j.NextRunTime.HasValue
                    && j.NextRunTime.Value <= now
                )
                .Take(batchSize)
                .ToList();

            foreach (var job in dueJobs)
            {
                job.Status = ScheduledJobStatus.Running;
                job.LockHolder = lockHolder;
                job.LockedAt = now;
            }

            return dueJobs;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<ScheduledJob?> GetJobByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var job = _jobs.Values.FirstOrDefault(j => string.Equals(j.Name, name, StringComparison.Ordinal));
        return Task.FromResult(job);
    }

    public Task<IReadOnlyList<ScheduledJob>> GetAllJobsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ScheduledJob> jobs = _jobs.Values.ToList();
        return Task.FromResult(jobs);
    }

    public async Task UpsertJobAsync(ScheduledJob job, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = _jobs.Values.FirstOrDefault(j => string.Equals(j.Name, job.Name, StringComparison.Ordinal));

            if (existing is not null)
            {
                // Match PostgreSQL semantics: only update definition fields, preserve runtime state
                existing.Type = job.Type;
                existing.CronExpression = job.CronExpression;
                existing.TimeZone = job.TimeZone;
                existing.Payload = job.Payload;
                existing.NextRunTime = job.NextRunTime;
                existing.RetryIntervals = job.RetryIntervals;
                existing.SkipIfRunning = job.SkipIfRunning;
                existing.IsEnabled = job.IsEnabled;
                existing.Timeout = job.Timeout;
                existing.MisfireStrategy = job.MisfireStrategy;
                existing.ConsumerTypeName = job.ConsumerTypeName;
                existing.DateUpdated = timeProvider.GetUtcNow();
            }
            else
            {
                _jobs[job.Id] = job;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task UpdateJobAsync(ScheduledJob job, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _jobs[job.Id] = job;
        return Task.CompletedTask;
    }

    public Task DeleteJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _jobs.TryRemove(jobId, out _);
        return Task.CompletedTask;
    }

    public Task CreateExecutionAsync(JobExecution execution, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _executions[execution.Id] = execution;
        return Task.CompletedTask;
    }

    public Task UpdateExecutionAsync(JobExecution execution, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _executions[execution.Id] = execution;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<JobExecution>> GetExecutionsAsync(
        Guid jobId,
        int limit,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<JobExecution> executions = _executions
            .Values.Where(e => e.JobId == jobId)
            .OrderByDescending(e => e.ScheduledTime)
            .Take(limit)
            .ToList();

        return Task.FromResult(executions);
    }

    public async Task<int> ReleaseStaleJobsAsync(TimeSpan staleness, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = timeProvider.GetUtcNow();
            var staleThreshold = now - staleness;
            var staleJobs = _jobs
                .Values.Where(j =>
                    j.Status == ScheduledJobStatus.Running && j.LockedAt.HasValue && j.LockedAt.Value < staleThreshold
                )
                .ToList();

            foreach (var job in staleJobs)
            {
                job.Status = ScheduledJobStatus.Pending;
                job.LockHolder = null;
                job.LockedAt = null;
            }

            return staleJobs.Count;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<int> PurgeExecutionsAsync(TimeSpan retention, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow();
        var retentionThreshold = now - retention;
        var toRemove = _executions
            .Values.Where(e => e.CompletedAt.HasValue && e.CompletedAt.Value < retentionThreshold)
            .Select(e => e.Id)
            .ToList();

        var removed = 0;
        foreach (var id in toRemove)
        {
            if (_executions.TryRemove(id, out _))
            {
                removed++;
            }
        }

        return Task.FromResult(removed);
    }

    public void Dispose() => _lock.Dispose();
}
