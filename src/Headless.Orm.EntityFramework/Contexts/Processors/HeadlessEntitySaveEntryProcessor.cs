// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel.DataAnnotations.Schema;
using Headless.Abstractions;
using Headless.Domain;
using Headless.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Headless.EntityFramework.Processors;

public sealed class HeadlessEntitySaveEntryProcessor(IGuidGenerator guidGenerator, ICurrentTenant currentTenant)
    : IHeadlessSaveEntryProcessor
{
    public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
    {
        switch (entry.State)
        {
            case EntityState.Added:
                _TrySetGuidId(entry);
                _TrySetMultiTenantId(entry);
                _TrySetConcurrencyStamp(entry);
                break;
            case EntityState.Modified:
                _TrySetConcurrencyStamp(entry);
                break;
        }
    }

    private void _TrySetGuidId(EntityEntry entry)
    {
        if (entry.Entity is not IEntity<Guid> entity || entity.Id != Guid.Empty)
        {
            return;
        }

        var idProperty = entry.Property(nameof(IEntity<>.Id)).Metadata.PropertyInfo!;

        if (
            idProperty.GetFirstOrDefaultAttribute<DatabaseGeneratedAttribute>() is
            { DatabaseGeneratedOption: not DatabaseGeneratedOption.None }
        )
        {
            return;
        }

        ObjectPropertiesHelper.TrySetProperty(entity, x => x.Id, guidGenerator.Create);
    }

    private void _TrySetMultiTenantId(EntityEntry entry)
    {
        if (entry.Entity is not IMultiTenant entity || !string.IsNullOrEmpty(entity.TenantId))
        {
            return;
        }

        if (entry.Property(nameof(IMultiTenant.TenantId)) is { IsModified: true, CurrentValue: not (null or "") })
        {
            return;
        }

        ObjectPropertiesHelper.TrySetProperty(entity, x => x.TenantId, () => currentTenant.Id);
    }

    private static void _TrySetConcurrencyStamp(EntityEntry entry)
    {
        if (entry.State == EntityState.Added)
        {
            if (entry.Entity is IHasConcurrencyStamp { ConcurrencyStamp: null } added)
            {
                ObjectPropertiesHelper.TrySetProperty(
                    added,
                    x => x.ConcurrencyStamp,
                    () => Guid.NewGuid().ToString("N")
                );
            }

            return;
        }

        if (entry.Entity is IHasConcurrencyStamp modified)
        {
            ObjectPropertiesHelper.TrySetProperty(
                modified,
                x => x.ConcurrencyStamp,
                () => Guid.NewGuid().ToString("N")
            );
        }
    }
}
