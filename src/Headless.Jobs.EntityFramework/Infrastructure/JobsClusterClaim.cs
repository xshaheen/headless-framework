// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Sql;
using Microsoft.EntityFrameworkCore;

namespace Headless.Jobs.Infrastructure;

/// <summary>
/// One claim statement of a cluster-gated claim: an extra <c>WHERE</c> fragment over the function column, its
/// parameters, and the most rows the statement may lease.
/// </summary>
internal readonly record struct JobsClusterClaimPart(string Filter, Func<DbParameter[]> Parameters, int BatchSize);

/// <summary>
/// Splits a native claim so no cluster-limited function is leased past its free slots. Runs inside the claim's
/// transaction: it locks each limited function the claim may touch, counts that function's live leased rows across
/// time jobs and cron occurrences, and returns one statement for every unlimited function plus one capped statement
/// per limited function with a free slot. The locks are held until the claim commits, so a concurrent claim on another
/// node counts this claim's rows before it leases its own.
/// </summary>
internal static class JobsClusterClaim
{
    // Function names are matched as the column stores them, in the database's default collation.
    private static readonly SqlColumnType _FunctionType = SqlColumnType.Text(0);

    /// <summary>
    /// Plans the claim statements. <paramref name="candidateFunctions"/> is the set of functions the claim can lease, or
    /// <see langword="null"/> when the claim selects its own rows; then every limited function the host runs is locked.
    /// </summary>
    public static async Task<JobsClusterClaimPart[]> PlanAsync<TTimeJob, TCronJob>(
        DbContext context,
        ISqlDialect dialect,
        JobsClusterConcurrency limits,
        JobsRunFilter runFilter,
        string functionColumn,
        IEnumerable<string>? candidateFunctions,
        int batchSize,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        if (!limits.HasLimits)
        {
            return [new(string.Empty, static () => [], batchSize)];
        }

        string[] limited;
        var includeUnlimited = true;
        if (candidateFunctions is null)
        {
            limited = [.. limits.LimitedFunctions.Where(runFilter.Allows)];
        }
        else
        {
            var candidates = candidateFunctions.Distinct(StringComparer.Ordinal).ToArray();
            limited = [.. candidates.Where(limits.IsLimited).Order(StringComparer.Ordinal)];
            includeUnlimited = limited.Length != candidates.Length;
        }

        if (limited.Length == 0)
        {
            return [new(string.Empty, static () => [], batchSize)];
        }

        var free = await ReserveAsync<TTimeJob, TCronJob>(context, limits, limited, cancellationToken)
            .ConfigureAwait(false);
        var parts = new List<JobsClusterClaimPart>(limited.Length + 1);
        if (includeUnlimited)
        {
            parts.Add(
                new(
                    $" AND NOT ({dialect.InList(functionColumn, "clusterLimited", _FunctionType)})",
                    () => [dialect.CreateListParameter("clusterLimited", _FunctionType, limited)],
                    batchSize
                )
            );
        }

        foreach (var function in limited)
        {
            if (free[function] is var slots and > 0)
            {
                parts.Add(
                    new(
                        $" AND {dialect.InList(functionColumn, "clusterFunction", _FunctionType)}",
                        () => [dialect.CreateListParameter("clusterFunction", _FunctionType, new[] { function })],
                        Math.Min(batchSize, slots)
                    )
                );
            }
        }

        return [.. parts];
    }

    /// <summary>
    /// Locks <paramref name="functions"/> (ordinal order, limited only) in the context's current transaction and returns
    /// each one's free slots.
    /// </summary>
    public static async Task<Dictionary<string, int>> ReserveAsync<TTimeJob, TCronJob>(
        DbContext context,
        JobsClusterConcurrency limits,
        string[] functions,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        if (functions.Length == 0)
        {
            return new(StringComparer.Ordinal);
        }

        await JobsKeyLock.AcquireClusterSlotsAsync(context, functions, cancellationToken).ConfigureAwait(false);
        var live = await _CountLiveAsync<TTimeJob, TCronJob>(context, functions, cancellationToken)
            .ConfigureAwait(false);

        return limits.FreeSlots(functions, live);
    }

    /// <summary>The limited functions with no free slot, counted without a lock.</summary>
    public static async Task<string[]> SaturatedAsync<TTimeJob, TCronJob>(
        DbContext context,
        JobsClusterConcurrency limits,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        var functions = limits.LimitedFunctions;
        var live = await _CountLiveAsync<TTimeJob, TCronJob>(context, functions, cancellationToken)
            .ConfigureAwait(false);

        return [.. limits.FreeSlots(functions, live).Where(x => x.Value == 0).Select(x => x.Key)];
    }

    private static async Task<Dictionary<string, int>> _CountLiveAsync<TTimeJob, TCronJob>(
        DbContext context,
        string[] functions,
        CancellationToken cancellationToken
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        // DateTime.UtcNow translates to the database clock. On PostgreSQL that is the transaction's start, which is no
        // later than now, so a lease that lapsed during the transaction still counts: the error only ever under-claims.
        var timeCounts = await context
            .Set<TTimeJob>()
            .AsNoTracking()
            .Where(x =>
                functions.Contains(x.Function)
                && (x.Status == JobStatus.Queued || x.Status == JobStatus.InProgress)
                && x.LockedUntil > DateTime.UtcNow
            )
            .GroupBy(x => x.Function)
            .Select(x => new { Function = x.Key, Count = x.Count() })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        var occurrenceCounts = await context
            .Set<CronJobOccurrenceEntity<TCronJob>>()
            .AsNoTracking()
            .Where(x =>
                functions.Contains(x.Function)
                && (x.Status == JobStatus.Queued || x.Status == JobStatus.InProgress)
                && x.LockedUntil > DateTime.UtcNow
            )
            .GroupBy(x => x.Function)
            .Select(x => new { Function = x.Key, Count = x.Count() })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var live = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var count in timeCounts.Concat(occurrenceCounts))
        {
            live[count.Function] = live.GetValueOrDefault(count.Function) + count.Count;
        }

        return live;
    }
}
