// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.Jobs.Infrastructure;

internal sealed record CronDefinitionRelationalMapping(
    string Table,
    string Id,
    string IsPaused,
    string ScheduleRevision
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
            Column(nameof(CronJobEntity.ScheduleRevision))
        );
    }
}

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
    string DateCreated,
    string DateUpdated
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
            Column(nameof(CronJobOccurrenceEntity<>.Id)),
            Column(nameof(CronJobOccurrenceEntity<>.Status)),
            Column(nameof(CronJobOccurrenceEntity<>.OwnerId)),
            Column(nameof(CronJobOccurrenceEntity<>.ExecutionTime)),
            Column(nameof(CronJobOccurrenceEntity<>.CronJobId)),
            Column(nameof(CronJobOccurrenceEntity<>.LockedUntil)),
            Column(nameof(CronJobOccurrenceEntity<>.OnNodeDeath)),
            Column(nameof(CronJobOccurrenceEntity<>.ElapsedTime)),
            Column(nameof(CronJobOccurrenceEntity<>.RetryCount)),
            Column(nameof(CronJobOccurrenceEntity<>.DateCreated)),
            Column(nameof(CronJobOccurrenceEntity<>.DateUpdated))
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
    string DateUpdated,
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
            Column(nameof(TimeJobEntity.DateUpdated)),
            Column(nameof(TimeJobEntity.ExecutionTime)),
            Column(nameof(TimeJobEntity.ParentId))
        );
    }
}
