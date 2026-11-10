// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.Jobs.Infrastructure;

#pragma warning disable MA0048 // A topic file: its types are peers with no main type, so the file is named for the topic.
// Only the columns the native claim strategies actually name in raw SQL belong here. The schedule-position columns
// are deliberately absent: the advance goes through EF's ExecuteUpdate, not this helper, and resolving a column here
// costs an EF metadata lookup on every cron claim batch. Add one back when a native-SQL consumer needs it.
internal sealed record CronDefinitionRelationalMapping(
    string Table,
    string Id,
    string IsPaused,
    string ScheduleRevision,
    string Function
)
{
    public static CronDefinitionRelationalMapping Create<TDbContext, TCronJob>(TDbContext dbContext)
        where TDbContext : DbContext
        where TCronJob : CronJobEntity
    {
        var entityType = typeof(TCronJob);
        var entity =
            dbContext.Model.FindEntityType(entityType)
            ?? throw new InvalidOperationException($"{entityType.Name} is not mapped by the Jobs DbContext.");
        var tableName =
            entity.GetTableName()
            ?? throw new InvalidOperationException($"{entityType.Name} is not mapped to a relational table.");
        var store = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
        var sql = dbContext.GetService<ISqlGenerationHelper>();

        string Column(string propertyName)
        {
            var property =
                entity.FindProperty(propertyName)
                ?? throw new InvalidOperationException($"{entityType.Name}.{propertyName} is not mapped.");
            var column =
                property.GetColumnName(store)
                ?? throw new InvalidOperationException($"{entityType.Name}.{propertyName} has no column mapping.");
            return sql.DelimitIdentifier(column);
        }

        return new CronDefinitionRelationalMapping(
            sql.DelimitIdentifier(tableName, entity.GetSchema()),
            Column(nameof(CronJobEntity.Id)),
            Column(nameof(CronJobEntity.IsPaused)),
            Column(nameof(CronJobEntity.ScheduleRevision)),
            Column(nameof(CronJobEntity.Function))
        );
    }
}

internal sealed record CronOccurrenceRelationalMapping(
    string Table,
    string Id,
    string Status,
    string ExecutionTime,
    string CronJobId,
    JobsLeaseColumns Lease,
    string OnNodeDeath,
    string ElapsedTime,
    string RetryCount,
    string CreatedAt,
    string UpdatedAt,
    string RecoveredFromUtc,
    string Disposition,
    string Function,
    string ContractVersion,
    string Request,
    string CorrelationId,
    string CausationId,
    string ChangedAt
)
{
    /// <summary>
    /// The occupied-instant accounting rule as a SQL predicate over one occurrence row, in the SAME shape the
    /// LINQ providers use via <see cref="CronOccurrenceAccounting.InstantViewSelector{TCronJob}" />: a row accounts
    /// for its instant unless it is the one status/disposition pair that owes another fire. The status and
    /// disposition literals are read off <see cref="CronOccurrenceAccounting" /> rather than spelled here, so the
    /// natives cannot drift from the rule.
    /// </summary>
    /// <param name="statusParameter">Placeholder bound to the unaccounted status name.</param>
    /// <param name="dispositionParameter">Placeholder bound to the unaccounted disposition name.</param>
    public string AccountsForInstantPredicate(string statusParameter, string dispositionParameter)
    {
        return $"NOT ({Status} = {statusParameter} AND {Disposition} = {dispositionParameter})";
    }

    /// <summary>The status literal an unaccounted row carries, for binding to the accounting predicate.</summary>
    public static string UnaccountedStatusValue => CronOccurrenceAccounting.UnaccountedStatus.ToString();

    /// <summary>The disposition literal an unaccounted row carries, for binding to the accounting predicate.</summary>
    public static string UnaccountedDispositionValue => CronOccurrenceAccounting.UnaccountedDisposition.ToString();

    public static CronOccurrenceRelationalMapping Create<TDbContext, TCronJob>(TDbContext dbContext)
        where TDbContext : DbContext
        where TCronJob : CronJobEntity
    {
        var entityType = typeof(CronJobOccurrenceEntity<TCronJob>);
        var entity =
            dbContext.Model.FindEntityType(entityType)
            ?? throw new InvalidOperationException($"{entityType.Name} is not mapped by the Jobs DbContext.");
        var tableName =
            entity.GetTableName()
            ?? throw new InvalidOperationException($"{entityType.Name} is not mapped to a relational table.");
        var store = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
        var sql = dbContext.GetService<ISqlGenerationHelper>();

        string Column(string propertyName)
        {
            var property =
                entity.FindProperty(propertyName)
                ?? throw new InvalidOperationException($"{entityType.Name}.{propertyName} is not mapped.");
            var column =
                property.GetColumnName(store)
                ?? throw new InvalidOperationException($"{entityType.Name}.{propertyName} has no column mapping.");
            return sql.DelimitIdentifier(column);
        }

        return new CronOccurrenceRelationalMapping(
            sql.DelimitIdentifier(tableName, entity.GetSchema()),
            Column(nameof(CronJobOccurrenceEntity<>.Id)),
            Column(nameof(CronJobOccurrenceEntity<>.Status)),
            Column(nameof(CronJobOccurrenceEntity<>.ExecutionTime)),
            Column(nameof(CronJobOccurrenceEntity<>.CronJobId)),
            JobsLeaseColumns.Create(entity, store, sql),
            Column(nameof(CronJobOccurrenceEntity<>.OnNodeDeath)),
            Column(nameof(CronJobOccurrenceEntity<>.ElapsedTime)),
            Column(nameof(CronJobOccurrenceEntity<>.RetryCount)),
            Column(nameof(CronJobOccurrenceEntity<>.CreatedAt)),
            Column(nameof(CronJobOccurrenceEntity<>.UpdatedAt)),
            // The native claim RETURNs/OUTPUTs this so a claimed row carries its recovery stamp out of the store
            // rather than trusting the caller to have supplied it.
            Column(nameof(CronJobOccurrenceEntity<>.RecoveredFromUtc)),
            Column(nameof(CronJobOccurrenceEntity<>.Disposition)),
            Column(nameof(CronJobOccurrenceEntity<>.Function)),
            Column(nameof(CronJobOccurrenceEntity<>.ContractVersion)),
            Column(nameof(CronJobOccurrenceEntity<>.Request)),
            Column(nameof(CronJobOccurrenceEntity<>.CorrelationId)),
            Column(nameof(CronJobOccurrenceEntity<>.CausationId)),
            // Every native claim stamps the change marker beside the claim, so a change read on any node sees it.
            Column(nameof(CronJobOccurrenceEntity<>.ChangedAt))
        );
    }
}

internal sealed record TimeJobRelationalMapping(
    string Table,
    string Id,
    string Status,
    JobsLeaseColumns Lease,
    string OnNodeDeath,
    string UpdatedAt,
    string ExecutionTime,
    string ParentId,
    string RunCondition,
    string Function,
    string ChangedAt
)
{
    public static TimeJobRelationalMapping Create<TDbContext, TTimeJob>(TDbContext dbContext)
        where TDbContext : DbContext
        where TTimeJob : TimeJobEntity<TTimeJob>
    {
        var entityType = typeof(TTimeJob);
        var entity =
            dbContext.Model.FindEntityType(entityType)
            ?? throw new InvalidOperationException($"{entityType.Name} is not mapped by the Jobs DbContext.");
        var tableName =
            entity.GetTableName()
            ?? throw new InvalidOperationException($"{entityType.Name} is not mapped to a relational table.");
        var store = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
        var sql = dbContext.GetService<ISqlGenerationHelper>();

        string Column(string propertyName)
        {
            var property =
                entity.FindProperty(propertyName)
                ?? throw new InvalidOperationException($"{entityType.Name}.{propertyName} is not mapped.");
            var column =
                property.GetColumnName(store)
                ?? throw new InvalidOperationException($"{entityType.Name}.{propertyName} has no column mapping.");
            return sql.DelimitIdentifier(column);
        }

        return new TimeJobRelationalMapping(
            sql.DelimitIdentifier(tableName, entity.GetSchema()),
            Column(nameof(TimeJobEntity.Id)),
            Column(nameof(TimeJobEntity.Status)),
            JobsLeaseColumns.Create(entity, store, sql),
            Column(nameof(TimeJobEntity.OnNodeDeath)),
            Column(nameof(TimeJobEntity.UpdatedAt)),
            Column(nameof(TimeJobEntity.ExecutionTime)),
            Column(nameof(TimeJobEntity.ParentId)),
            Column(nameof(TimeJobEntity.RunCondition)),
            Column(nameof(TimeJobEntity.Function)),
            Column(nameof(TimeJobEntity.ChangedAt))
        );
    }
}

/// <summary>The delimited columns of a job row's lease, for native SQL that reads the lease in a filter.</summary>
internal sealed record JobsLeaseColumns(
    string Generation,
    string State,
    string ExpiresAt,
    string Holder,
    string TakeoverCount
)
{
    public static JobsLeaseColumns Create(IEntityType entity, StoreObjectIdentifier store, ISqlGenerationHelper sql)
    {
        var lease =
            entity.FindComplexProperty(nameof(TimeJobEntity.Lease))
            ?? throw new InvalidOperationException($"{entity.ClrType.Name}.Lease is not mapped.");

        string Column(string propertyName)
        {
            var column =
                lease.ComplexType.FindProperty(propertyName)?.GetColumnName(store)
                ?? throw new InvalidOperationException(
                    $"{entity.ClrType.Name}.Lease.{propertyName} has no column mapping."
                );
            return sql.DelimitIdentifier(column);
        }

        return new JobsLeaseColumns(
            Column(nameof(RowLeaseSnapshot.Generation)),
            Column(nameof(RowLeaseSnapshot.State)),
            Column(nameof(RowLeaseSnapshot.ExpiresAt)),
            Column(nameof(RowLeaseSnapshot.Holder)),
            Column(nameof(RowLeaseSnapshot.TakeoverCount))
        );
    }
}
