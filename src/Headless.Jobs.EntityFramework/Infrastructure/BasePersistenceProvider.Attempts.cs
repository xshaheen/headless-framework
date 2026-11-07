// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Headless.Jobs.Infrastructure;

// Attempt history: the open, the reads, and the helpers that close an attempt in the transaction of the write that
// moves its run out of InProgress. A model without the attempt tables (a consumer-managed model that skipped
// FinalizeJobsModel) keeps every write exactly as it was, autocommit and single-statement.
internal abstract partial class BasePersistenceProvider<TDbContext, TTimeJob, TCronJob>
{
    private const int _MappingUnknown = 0;
    private const int _Mapped = 1;
    private const int _NotMapped = 2;

    // The reasons the sweeps write on the run; an attempt they close carries the same text.
    private const string _NodeNotAliveReason = "Node is not alive!";
    private const string _LeaseLapsedReason = "Lease lapsed while running!";

    private int _timeJobAttemptsMapping = _MappingUnknown;
    private int _occurrenceAttemptsMapping = _MappingUnknown;

    public async Task<bool> OpenTimeJobAttemptAsync(
        Guid timeJobId,
        string claimOwner,
        DateTime startedAt,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(claimOwner);

        if (!_AttemptsMapped<TimeJobAttemptEntity>())
        {
            return false;
        }

        return await _OpenAttemptAsync<TimeJobAttemptEntity>(
                timeJobId,
                claimOwner,
                startedAt,
                // Stamping the change marker shows the open in change reads and locks the run row, so the open waits
                // for, or blocks, any write that moves the run out of InProgress and closes its attempt.
                (dbContext, ct) =>
                    dbContext
                        .Set<TTimeJob>()
                        .Where(x => x.Id == timeJobId)
                        .WhereRunningBy(claimOwner)
                        .ExecuteUpdateAsync(setter => setter.SetChangedAt(), ct),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<bool> OpenCronJobOccurrenceAttemptAsync(
        Guid occurrenceId,
        string claimOwner,
        DateTime startedAt,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(claimOwner);

        if (!_AttemptsMapped<CronJobOccurrenceAttemptEntity>())
        {
            return false;
        }

        return await _OpenAttemptAsync<CronJobOccurrenceAttemptEntity>(
                occurrenceId,
                claimOwner,
                startedAt,
                (dbContext, ct) =>
                    dbContext
                        .Set<CronJobOccurrenceEntity<TCronJob>>()
                        .Where(x => x.Id == occurrenceId)
                        .WhereRunningBy(claimOwner)
                        .ExecuteUpdateAsync(setter => setter.SetChangedAt(), ct),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public Task<IReadOnlyList<TimeJobAttemptEntity>> GetTimeJobAttemptsAsync(
        Guid timeJobId,
        CancellationToken cancellationToken = default
    )
    {
        return _GetAttemptsAsync<TimeJobAttemptEntity>(timeJobId, cancellationToken);
    }

    public Task<IReadOnlyList<CronJobOccurrenceAttemptEntity>> GetCronJobOccurrenceAttemptsAsync(
        Guid occurrenceId,
        CancellationToken cancellationToken = default
    )
    {
        return _GetAttemptsAsync<CronJobOccurrenceAttemptEntity>(occurrenceId, cancellationToken);
    }

    private async Task<IReadOnlyList<TAttempt>> _GetAttemptsAsync<TAttempt>(
        Guid runId,
        CancellationToken cancellationToken
    )
        where TAttempt : JobAttemptEntity
    {
        if (!_AttemptsMapped<TAttempt>())
        {
            return [];
        }

        await using var dbContext = await DbContextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        return await JobsAttemptLedger.ReadAsync<TAttempt>(dbContext, runId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> _OpenAttemptAsync<TAttempt>(
        Guid runId,
        string claimOwner,
        DateTime startedAt,
        Func<TDbContext, CancellationToken, Task<int>> lockRunningRow,
        CancellationToken cancellationToken
    )
        where TAttempt : JobAttemptEntity, new()
    {
        // Its own short transaction, apart from the claim: a claim writes the lease deadline, which must stay
        // autocommit (see the class-level note), and this transaction writes no deadline.
        return await JobsStoreTransaction
            .ExecuteAsync<TDbContext, bool>(
                DbContextFactory,
                async (attempt, ct) =>
                {
                    if (await lockRunningRow(attempt.DbContext, ct).ConfigureAwait(false) == 0)
                    {
                        return false;
                    }

                    await JobsAttemptLedger
                        .OpenAsync<TAttempt>(attempt.DbContext, runId, claimOwner, startedAt, ct)
                        .ConfigureAwait(false);
                    await attempt.CommitAsync(ct).ConfigureAwait(false);

                    return true;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a write that ends one run's execution, and when it applied, closes the run's attempt in the same
    /// transaction.
    /// </summary>
    private async Task<int> _WriteEndingAttemptAsync<TAttempt>(
        Func<TDbContext, CancellationToken, Task<int>> write,
        Func<TDbContext, CancellationToken, Task> closeAttempt,
        CancellationToken cancellationToken
    )
        where TAttempt : JobAttemptEntity
    {
        if (!_AttemptsMapped<TAttempt>())
        {
            await using var dbContext = await DbContextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);

            return await write(dbContext, cancellationToken).ConfigureAwait(false);
        }

        return await JobsStoreTransaction
            .ExecuteAsync<TDbContext, int>(
                DbContextFactory,
                async (attempt, ct) =>
                {
                    var affected = await write(attempt.DbContext, ct).ConfigureAwait(false);
                    if (affected > 0)
                    {
                        await closeAttempt(attempt.DbContext, ct).ConfigureAwait(false);
                    }

                    await attempt.CommitAsync(ct).ConfigureAwait(false);

                    return affected;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Applies a sweep arm that moves running rows out of InProgress, inside the sweep's transaction, and closes the
    /// attempts of the rows it moved with <paramref name="end"/>.
    /// </summary>
    /// <remarks>
    /// The arm first reads the rows it would move, then moves only those, still under its own predicate. When every one
    /// moved, each gets its attempt closed, or inserted closed when its open was lost. When a concurrent writer moved
    /// some first (another node running the same sweep, or the owner ending the run), the rows this arm moved cannot be
    /// told apart from those, so only attempts still open on rows no longer running are closed: that writer closed its
    /// own, and nothing is closed twice.
    /// </remarks>
    private async Task<int> _MoveRunningRowsAsync<TRun, TAttempt>(
        TDbContext dbContext,
        IQueryable<TRun> rows,
        Action<UpdateSettersBuilder<TRun>> setters,
        JobAttemptEnd end
    )
        where TRun : class
        where TAttempt : JobAttemptEntity, new()
    {
        if (!_AttemptsMapped<TAttempt>())
        {
            return await rows.ExecuteUpdateAsync(setters, CancellationToken.None).ConfigureAwait(false);
        }

        var candidates = await rows.AsNoTracking()
            .Select(x => new { Id = EF.Property<Guid>(x, "Id"), OwnerId = EF.Property<string?>(x, "OwnerId") })
            .ToListAsync(CancellationToken.None)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return 0;
        }

        var ids = candidates.ConvertAll(static candidate => candidate.Id);
        var affected = await rows.Where(x => ids.Contains(EF.Property<Guid>(x, "Id")))
            .ExecuteUpdateAsync(setters, CancellationToken.None)
            .ConfigureAwait(false);

        if (affected == candidates.Count)
        {
            await JobsAttemptLedger
                .CloseAsync<TAttempt>(
                    dbContext,
                    candidates.ConvertAll(static candidate => (candidate.Id, candidate.OwnerId)),
                    end,
                    insertWhenMissing: true,
                    CancellationToken.None
                )
                .ConfigureAwait(false);
        }
        else if (affected > 0)
        {
            var moved = await dbContext
                .Set<TRun>()
                .AsNoTracking()
                .Where(x =>
                    ids.Contains(EF.Property<Guid>(x, "Id"))
                    && EF.Property<JobStatus>(x, "Status") != JobStatus.InProgress
                )
                .Select(x => EF.Property<Guid>(x, "Id"))
                .ToListAsync(CancellationToken.None)
                .ConfigureAwait(false);

            await JobsAttemptLedger
                .CloseAsync<TAttempt>(
                    dbContext,
                    moved.ConvertAll(static id => (id, (string?)null)),
                    end,
                    insertWhenMissing: false,
                    CancellationToken.None
                )
                .ConfigureAwait(false);
        }

        return affected;
    }

    /// <summary>The attempt end an executor write carries, or <see langword="null"/> when the write ends none.</summary>
    private static JobAttemptEnd? _EndedAttempt(JobExecutionState state)
    {
        return state.AttemptEnd is { } end && state.PropertiesToUpdate.Contains(nameof(JobExecutionState.AttemptEnd))
            ? end
            : null;
    }

    /// <summary>The end of an attempt the store ends itself, stamped now on the store's injected clock.</summary>
    private JobAttemptEnd _StoreAttemptEnd(JobAttemptOutcome outcome, string? reason, string? failureFingerprint = null)
    {
        return new JobAttemptEnd
        {
            Outcome = outcome,
            EndedAt = TimeProvider.GetUtcNow().UtcDateTime,
            ExceptionMessage = reason,
            FailureFingerprint = failureFingerprint,
        };
    }

    /// <summary>
    /// The end of an attempt a graceful stop settled, by the status the settle wrote: a hand-back to Idle is a release,
    /// and Failed and Skipped follow the run's node-death policy.
    /// </summary>
    private JobAttemptEnd _SettledAttemptEnd(JobStatus settledStatus, string reason)
    {
        return settledStatus switch
        {
            JobStatus.Failed => _StoreAttemptEnd(
                JobAttemptOutcome.Failed,
                reason,
                JobFailureFingerprints.InterruptedByShutdown
            ),
            JobStatus.Skipped => _StoreAttemptEnd(JobAttemptOutcome.Skipped, reason),
            _ => _StoreAttemptEnd(JobAttemptOutcome.Released, reason),
        };
    }

    private bool _AttemptsMapped<TAttempt>()
        where TAttempt : JobAttemptEntity
    {
        ref var mapping = ref typeof(TAttempt) == typeof(TimeJobAttemptEntity)
            ? ref _timeJobAttemptsMapping
            : ref _occurrenceAttemptsMapping;

        var known = Volatile.Read(ref mapping);
        if (known != _MappingUnknown)
        {
            return known == _Mapped;
        }

        // The model is fixed for the context type, so one look answers for the provider's lifetime.
        using var dbContext = DbContextFactory.CreateDbContext();
        var mapped = JobsAttemptLedger.IsMapped<TAttempt>(dbContext);
        Volatile.Write(ref mapping, mapped ? _Mapped : _NotMapped);

        return mapped;
    }
}
