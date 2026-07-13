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
    string UpdatedAt
)
{
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
            Column(nameof(CronJobOccurrenceEntity<TCronJob>.Id)),
            Column(nameof(CronJobOccurrenceEntity<TCronJob>.Status)),
            Column(nameof(CronJobOccurrenceEntity<TCronJob>.OwnerId)),
            Column(nameof(CronJobOccurrenceEntity<TCronJob>.ExecutionTime)),
            Column(nameof(CronJobOccurrenceEntity<TCronJob>.CronJobId)),
            Column(nameof(CronJobOccurrenceEntity<TCronJob>.LockedUntil)),
            Column(nameof(CronJobOccurrenceEntity<TCronJob>.OnNodeDeath)),
            Column(nameof(CronJobOccurrenceEntity<TCronJob>.ElapsedTime)),
            Column(nameof(CronJobOccurrenceEntity<TCronJob>.RetryCount)),
            Column(nameof(CronJobOccurrenceEntity<TCronJob>.CreatedAt)),
            Column(nameof(CronJobOccurrenceEntity<TCronJob>.UpdatedAt))
        );
    }
}

internal sealed record TimeJobRelationalMapping(
    string Table,
    string Id,
    string Status,
    string OwnerId,
    string LockedUntil,
    string OnNodeDeath,
    string UpdatedAt,
    string ExecutionTime,
    string ParentId
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
            Column(nameof(TimeJobEntity.OwnerId)),
            Column(nameof(TimeJobEntity.LockedUntil)),
            Column(nameof(TimeJobEntity.OnNodeDeath)),
            Column(nameof(TimeJobEntity.UpdatedAt)),
            Column(nameof(TimeJobEntity.ExecutionTime)),
            Column(nameof(TimeJobEntity.ParentId))
        );
    }
}
