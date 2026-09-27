// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Headless.EntityFramework.Contexts.Runtime;

/// <summary>
/// Refuses, in a context placed in a tenant's own schema, any table mapped to an explicit other schema. A tenant-owned
/// table there would hold every tenant's rows side by side behind only the query filter; a shared table would be
/// created again by every tenant's migrations (each tenant keeps its own migrations history) and collide. Shared
/// tables belong in a context that is not tenant-routed.
/// </summary>
internal sealed class HeadlessTenantPlacementModelConvention(string schema) : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context
    )
    {
        foreach (var entity in modelBuilder.Metadata.GetEntityTypes())
        {
            // Every entity type with a table is checked, derived and owned ones included: a TPT/TPC derived type or an
            // owned collection can map to a table of its own in another schema.
            if (entity.GetTableName() is null || string.Equals(entity.GetSchema(), schema, StringComparison.Ordinal))
            {
                continue;
            }

            var reason = entity.IsTenantOwned()
                ? "every tenant's rows would share one table"
                : "every tenant's migrations would create it again";

            throw new InvalidOperationException(
                $"Entity '{entity.DisplayName()}' is mapped to schema '{entity.GetSchema() ?? "<default>"}' in a "
                    + $"context placed in tenant schema '{schema}', so {reason}. Remove the explicit schema from its "
                    + "table mapping, or move shared tables to a context that is not tenant-routed."
            );
        }
    }
}
