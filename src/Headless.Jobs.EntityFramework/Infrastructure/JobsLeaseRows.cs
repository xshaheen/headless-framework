// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Fencing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.Jobs.Infrastructure;

/// <summary>
/// Binds a Jobs context to the <c>FencedRows</c> transitions: the descriptor of each job table, the row a claim
/// handed out re-addressed to it, and the connection and transaction the transition runs on.
/// </summary>
internal static class JobsLeaseRows
{
    /// <summary>The descriptor of the time-job table <paramref name="context"/> maps.</summary>
    public static FencedRowTable<Guid> TimeJobs<TTimeJob>(DbContext context)
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        return context.FencedRowTable<TTimeJob, Guid>(static e => e.Lease, JobsSqlDialects.Resolve(context));
    }

    /// <summary>The descriptor of the cron-occurrence table <paramref name="context"/> maps.</summary>
    public static FencedRowTable<Guid> CronOccurrences<TCronJob>(DbContext context)
        where TCronJob : CronJobEntity
    {
        return context.FencedRowTable<CronJobOccurrenceEntity<TCronJob>, Guid>(
            static e => e.Lease,
            JobsSqlDialects.Resolve(context)
        );
    }

    /// <summary>
    /// <paramref name="row"/> addressed to <paramref name="table"/>. A claim names its rows by the logical table name
    /// (<see cref="JobsLeaseTables"/>), which the store may map to another schema or table name.
    /// </summary>
    public static FencedRow<Guid> On(FencedRowTable<Guid> table, FencedRow<Guid> row)
    {
        return string.Equals(row.Table, table.Name, StringComparison.Ordinal)
            ? row
            : new FencedRow<Guid>(table.Name, row.Key, row.Generation);
    }

    /// <summary>The logical row a claim hands out for a row of the time-job table.</summary>
    public static FencedRow<Guid> TimeJob(Guid id, long generation) => JobsLeaseTables.TimeJob(id, generation);

    /// <summary>The logical row a claim hands out for a row of the cron-occurrence table.</summary>
    public static FencedRow<Guid> CronOccurrence(Guid id, long generation) =>
        JobsLeaseTables.CronOccurrence(id, generation);

    /// <summary>The context's connection, the one a transition runs on.</summary>
    public static DbConnection Connection(DbContext context) => context.Database.GetDbConnection();

    /// <summary>The context's open transaction, or <see langword="null"/> for a transition that commits on its own.</summary>
    public static DbTransaction? Transaction(DbContext context) =>
        context.Database.CurrentTransaction?.GetDbTransaction();
}
