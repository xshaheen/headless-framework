// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.Jobs.Infrastructure;

internal sealed record TimeJobRelationalMapping(
    string Table,
    string Id,
    string Status,
    string OwnerId,
    string LockedUntil,
    string OnNodeDeath,
    string UpdatedAt,
    string ExecutionTime,
    string ParentId,
    string RunCondition,
    string Function
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
            Column(nameof(TimeJobEntity.ParentId)),
            Column(nameof(TimeJobEntity.RunCondition)),
            Column(nameof(TimeJobEntity.Function))
        );
    }
}
