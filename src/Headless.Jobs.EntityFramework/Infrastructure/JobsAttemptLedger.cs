// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Headless.Jobs.Infrastructure;

/// <summary>
/// The attempt-history writes, on a context its caller already enlisted in the transaction that moved the run.
/// </summary>
/// <remarks>
/// Every caller first writes the run row (the open stamps its change marker, a close ends or hands back the run), so
/// the run row's lock serializes every attempt write of one run: two writers can never both close one attempt or
/// number two attempts alike.
/// </remarks>
internal static class JobsAttemptLedger
{
    /// <summary>Whether <paramref name="dbContext"/>'s model maps the <typeparamref name="TAttempt"/> table.</summary>
    public static bool IsMapped<TAttempt>(DbContext dbContext)
        where TAttempt : JobAttemptEntity
    {
        return dbContext.Model.FindEntityType(typeof(TAttempt)) is not null;
    }

    /// <summary>Inserts the run's next attempt, open, numbered one past its last.</summary>
    public static async Task OpenAsync<TAttempt>(
        DbContext dbContext,
        Guid runId,
        string ownerId,
        DateTime startedAt,
        CancellationToken cancellationToken
    )
        where TAttempt : JobAttemptEntity, new()
    {
        var last = await dbContext
            .Set<TAttempt>()
            .Where(attempt => attempt.RunId == runId)
            .MaxAsync(attempt => (int?)attempt.Number, cancellationToken)
            .ConfigureAwait(false);

        dbContext
            .Set<TAttempt>()
            .Add(
                new TAttempt
                {
                    RunId = runId,
                    Number = (last ?? 0) + 1,
                    OwnerId = ownerId,
                    StartedAt = startedAt,
                }
            );
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Closes the open attempt of each run in <paramref name="runs"/> with <paramref name="end"/>; when
    /// <paramref name="insertWhenMissing"/> is set, a run with no open attempt gets one inserted closed, numbered one
    /// past its last, with the owner its caller knows.
    /// </summary>
    public static async Task CloseAsync<TAttempt>(
        DbContext dbContext,
        IReadOnlyCollection<(Guid RunId, string? OwnerId)> runs,
        JobAttemptEnd end,
        bool insertWhenMissing,
        CancellationToken cancellationToken
    )
        where TAttempt : JobAttemptEntity, new()
    {
        if (runs.Count == 0)
        {
            return;
        }

        var set = dbContext.Set<TAttempt>();
        var ids = runs.Select(static run => run.RunId).Distinct().ToList();
        var open = await set.AsNoTracking()
            .Where(attempt => ids.Contains(attempt.RunId) && attempt.EndedAt == null)
            .Select(attempt => attempt.RunId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (open.Count != 0)
        {
            await set.Where(attempt => open.Contains(attempt.RunId) && attempt.EndedAt == null)
                .ExecuteUpdateAsync(
                    setter =>
                        setter
                            .SetProperty(attempt => attempt.EndedAt, (DateTime?)end.EndedAt)
                            .SetProperty(attempt => attempt.Outcome, (JobAttemptOutcome?)end.Outcome)
                            .SetProperty(attempt => attempt.ExceptionMessage, end.ExceptionMessage)
                            .SetProperty(attempt => attempt.FailureFingerprint, end.FailureFingerprint),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        if (!insertWhenMissing)
        {
            return;
        }

        var missing = runs.Where(run => !open.Contains(run.RunId)).DistinctBy(static run => run.RunId).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var missingIds = missing.ConvertAll(static run => run.RunId);
        var last = await set.AsNoTracking()
            .Where(attempt => missingIds.Contains(attempt.RunId))
            .GroupBy(attempt => attempt.RunId)
            .Select(group => new { RunId = group.Key, Last = group.Max(attempt => attempt.Number) })
            .ToDictionaryAsync(row => row.RunId, row => row.Last, cancellationToken)
            .ConfigureAwait(false);

        foreach (var (runId, ownerId) in missing)
        {
            set.Add(
                new TAttempt
                {
                    RunId = runId,
                    Number = last.GetValueOrDefault(runId) + 1,
                    OwnerId = ownerId,
                    StartedAt = end.StartedAt,
                    EndedAt = end.EndedAt,
                    Outcome = end.Outcome,
                    ExceptionMessage = end.ExceptionMessage,
                    FailureFingerprint = end.FailureFingerprint,
                }
            );
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the run's attempts in attempt order.</summary>
    public static async Task<IReadOnlyList<TAttempt>> ReadAsync<TAttempt>(
        DbContext dbContext,
        Guid runId,
        CancellationToken cancellationToken
    )
        where TAttempt : JobAttemptEntity
    {
        return await dbContext
            .Set<TAttempt>()
            .AsNoTracking()
            .Where(attempt => attempt.RunId == runId)
            .OrderBy(attempt => attempt.Number)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
