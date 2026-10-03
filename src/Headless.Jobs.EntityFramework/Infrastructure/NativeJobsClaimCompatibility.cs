// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Abstractions;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Internal;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable MA0133 // EF must keep DateTime.UtcNow in expression trees so providers translate the database clock before the DateTimeOffset assignment.
namespace Headless.Jobs.Infrastructure;

internal static class NativeJobsClaimCompatibility
{
    public static string? FindIncompatibility<TTimeJob, TCronJob>(IModel model)
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        Type[] jobTypes = [typeof(TTimeJob), typeof(TCronJob), typeof(CronJobOccurrenceEntity<TCronJob>)];
        var entityTypes = jobTypes.Select(model.FindEntityType).Where(x => x is not null).Cast<IEntityType>().ToArray();

        if (entityTypes.FirstOrDefault(x => x.GetDeclaredQueryFilters().Count > 0) is { } filtered)
        {
            return $"entity {filtered.DisplayName()} has a global query filter";
        }

        if (entityTypes.FirstOrDefault(x => x.GetDiscriminatorPropertyName() is not null) is { } discriminated)
        {
            return $"entity {discriminated.DisplayName()} uses discriminator-based inheritance";
        }

        foreach (var entityType in entityTypes)
        {
            var tableName = entityType.GetTableName();
            if (tableName is null)
            {
                continue;
            }

            var schema = entityType.GetSchema();
            var sharesTable = model
                .GetEntityTypes()
                .Any(other =>
                    other != entityType
                    && string.Equals(other.GetTableName(), tableName, StringComparison.Ordinal)
                    && string.Equals(other.GetSchema(), schema, StringComparison.Ordinal)
                );
            if (sharesTable)
            {
                return $"entity {entityType.DisplayName()} shares table {schema ?? "<default>"}.{tableName}";
            }
        }

        return null;
    }
}
