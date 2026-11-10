// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;

namespace Headless.Jobs.Provider;

// Attempt history. Each run's attempts live in a list under one lock. A close runs only after the compare-and-swap
// that moved the run won, so an attempt never closes twice. An open checks the run and appends under the same lock,
// so a sweep that moves the run while the open is in flight closes the attempt the open appended instead of missing it.
internal sealed partial class JobsInMemoryPersistenceProvider<TTimeJob, TCronJob>
{
    private const string _LeaseLapsedReason = "Lease lapsed while running!";
    private const string _NodeNotAliveReason = "Node is not alive!";

    private readonly Lock _attemptsLock = new();
    private readonly Dictionary<Guid, List<TimeJobAttemptEntity>> _timeJobAttempts = [];
    private readonly Dictionary<Guid, List<CronJobOccurrenceAttemptEntity>> _occurrenceAttempts = [];

    public Task<bool> OpenTimeJobAttemptAsync(
        FencedRow<Guid> row,
        string claimOwner,
        DateTime startedAt,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_attemptsLock)
        {
            // The open shows in change reads like any other write to the run, so it stamps the run's marker.
            while (
                _timeJobs.TryGetValue(row.Key, out var current)
                && string.Equals(current.Lease.Holder, claimOwner, StringComparison.Ordinal)
                && _IsHeldRun(current.Status, current.Lease, row.Generation)
            )
            {
                var updated = _CloneTicker(current);
                updated.ChangedAt = _NextChangeMarker();
                if (_TryUpdateTimeJob(row.Key, updated, current))
                {
                    _OpenAttempt(_timeJobAttempts, row.Key, claimOwner, startedAt);
                    return Task.FromResult(true);
                }
            }
        }

        return Task.FromResult(false);
    }

    public Task<bool> OpenCronJobOccurrenceAttemptAsync(
        FencedRow<Guid> row,
        string claimOwner,
        DateTime startedAt,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_attemptsLock)
        {
            while (
                _cronOccurrences.TryGetValue(row.Key, out var current)
                && string.Equals(current.Lease.Holder, claimOwner, StringComparison.Ordinal)
                && _IsHeldRun(current.Status, current.Lease, row.Generation)
            )
            {
                var updated = _CloneCronOccurrence(current);
                updated.ChangedAt = _NextChangeMarker();
                if (_cronOccurrences.TryUpdate(row.Key, updated, current))
                {
                    _OpenAttempt(_occurrenceAttempts, row.Key, claimOwner, startedAt);
                    return Task.FromResult(true);
                }
            }
        }

        return Task.FromResult(false);
    }

    // An open fences on the claim's generation still holding an active lease, as the relational store does; expiry alone
    // does not refuse it, since the open extends nothing and the sweep that ends the lease closes the attempt.
    private static bool _IsHeldRun(JobStatus status, RowLeaseSnapshot lease, long generation) =>
        status == JobStatus.InProgress && lease.State == RowLeaseState.Active && lease.Generation == generation;

    public Task<IReadOnlyList<TimeJobAttemptEntity>> GetTimeJobAttemptsAsync(
        Guid timeJobId,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult<IReadOnlyList<TimeJobAttemptEntity>>(_ReadAttempts(_timeJobAttempts, timeJobId));
    }

    public Task<IReadOnlyList<CronJobOccurrenceAttemptEntity>> GetCronJobOccurrenceAttemptsAsync(
        Guid occurrenceId,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult<IReadOnlyList<CronJobOccurrenceAttemptEntity>>(
            _ReadAttempts(_occurrenceAttempts, occurrenceId)
        );
    }

    private static JobAttemptEnd _StoreAttemptEnd(
        JobAttemptOutcome outcome,
        DateTimeOffset now,
        string? reason,
        string? failureFingerprint = null
    ) =>
        new()
        {
            Outcome = outcome,
            EndedAt = now.UtcDateTime,
            ExceptionMessage = reason,
            FailureFingerprint = failureFingerprint,
        };

    // A graceful-stop release (a policy override) hands back a run whose handler never started, so it closes an open
    // attempt but never inserts one. A settle ends a started run by its policy and inserts a closed attempt if needed.
    private void _CloseInterruptedAttempt<TAttempt>(
        Dictionary<Guid, List<TAttempt>> store,
        Guid runId,
        string? claimOwner,
        string? reason,
        NodeDeathPolicy? policyOverride,
        NodeDeathPolicy policy,
        DateTimeOffset now
    )
        where TAttempt : JobAttemptEntity, new()
    {
        var end = (policyOverride ?? policy) switch
        {
            NodeDeathPolicy.MarkFailed => _StoreAttemptEnd(
                JobAttemptOutcome.Failed,
                now,
                reason,
                JobFailureFingerprints.InterruptedByShutdown
            ),
            NodeDeathPolicy.Skip => _StoreAttemptEnd(JobAttemptOutcome.Skipped, now, reason),
            _ => _StoreAttemptEnd(JobAttemptOutcome.Released, now, reason),
        };

        _CloseAttempt(store, runId, claimOwner, end, insertWhenMissing: policyOverride is null);
    }

    private void _CloseExecutorAttempt<TAttempt>(Dictionary<Guid, List<TAttempt>> store, JobExecutionState state)
        where TAttempt : JobAttemptEntity, new()
    {
        if (state.AttemptEnd is { } end && state.PropertiesToUpdate.Contains(nameof(JobExecutionState.AttemptEnd)))
        {
            _CloseAttempt(store, state.JobId, state.ClaimOwner, end, insertWhenMissing: true);
        }
    }

    private static void _OpenAttempt<TAttempt>(
        Dictionary<Guid, List<TAttempt>> store,
        Guid runId,
        string claimOwner,
        DateTime startedAt
    )
        where TAttempt : JobAttemptEntity, new()
    {
        var attempts = _AttemptsOf(store, runId);
        attempts.Add(
            new TAttempt
            {
                RunId = runId,
                Number = attempts.Count + 1,
                OwnerId = claimOwner,
                StartedAt = startedAt,
            }
        );
    }

    private void _CloseAttempt<TAttempt>(
        Dictionary<Guid, List<TAttempt>> store,
        Guid runId,
        string? ownerId,
        JobAttemptEnd end,
        bool insertWhenMissing
    )
        where TAttempt : JobAttemptEntity, new()
    {
        lock (_attemptsLock)
        {
            var attempts = _AttemptsOf(store, runId);
            var closed = false;

            foreach (var attempt in attempts.Where(static attempt => attempt.EndedAt is null))
            {
                _Apply(attempt, end);
                closed = true;
            }

            if (closed || !insertWhenMissing)
            {
                return;
            }

            // The open was lost: record the attempt closed, with what the closing writer knows.
            var inserted = new TAttempt
            {
                RunId = runId,
                Number = attempts.Count + 1,
                OwnerId = ownerId,
                StartedAt = end.StartedAt,
            };
            _Apply(inserted, end);
            attempts.Add(inserted);
        }

        static void _Apply(TAttempt attempt, JobAttemptEnd end)
        {
            attempt.EndedAt = end.EndedAt;
            attempt.Outcome = end.Outcome;
            attempt.ExceptionMessage = end.ExceptionMessage;
            attempt.FailureFingerprint = end.FailureFingerprint;
        }
    }

    private static List<TAttempt> _AttemptsOf<TAttempt>(Dictionary<Guid, List<TAttempt>> store, Guid runId)
    {
        if (!store.TryGetValue(runId, out var attempts))
        {
            attempts = [];
            store.Add(runId, attempts);
        }

        return attempts;
    }

    // Copies, so a caller cannot change stored history.
    private TAttempt[] _ReadAttempts<TAttempt>(Dictionary<Guid, List<TAttempt>> store, Guid runId)
        where TAttempt : JobAttemptEntity, new()
    {
        lock (_attemptsLock)
        {
            if (!store.TryGetValue(runId, out var attempts))
            {
                return [];
            }

            return
            [
                .. attempts.Select(static attempt => new TAttempt
                {
                    RunId = attempt.RunId,
                    Number = attempt.Number,
                    OwnerId = attempt.OwnerId,
                    StartedAt = attempt.StartedAt,
                    EndedAt = attempt.EndedAt,
                    Outcome = attempt.Outcome,
                    ExceptionMessage = attempt.ExceptionMessage,
                    FailureFingerprint = attempt.FailureFingerprint,
                }),
            ];
        }
    }

    // A deleted run takes its attempts with it, as the relational store's cascading key does.
    private void _RemoveAttempts<TAttempt>(Dictionary<Guid, List<TAttempt>> store, Guid runId)
    {
        lock (_attemptsLock)
        {
            store.Remove(runId);
        }
    }

    private bool _TryRemoveOccurrence(Guid occurrenceId)
    {
        if (!_cronOccurrences.TryRemove(occurrenceId, out _))
        {
            return false;
        }

        _RemoveAttempts(_occurrenceAttempts, occurrenceId);
        return true;
    }
}
