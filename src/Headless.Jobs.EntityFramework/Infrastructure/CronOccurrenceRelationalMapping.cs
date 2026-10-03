// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.Jobs.Infrastructure;

internal sealed record CronOccurrenceRelationalMapping(
    string Table,
    string Id,
    string Status,
    string OwnerId,
    string ExecutionTime,
    string CronJobId,
    string LockedUntil,
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
    string CausationId
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
            Column(nameof(CronJobOccurrenceEntity<>.OwnerId)),
            Column(nameof(CronJobOccurrenceEntity<>.ExecutionTime)),
            Column(nameof(CronJobOccurrenceEntity<>.CronJobId)),
            Column(nameof(CronJobOccurrenceEntity<>.LockedUntil)),
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
            Column(nameof(CronJobOccurrenceEntity<>.CausationId))
        );
    }
}
