// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.Jobs.Infrastructure;

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
