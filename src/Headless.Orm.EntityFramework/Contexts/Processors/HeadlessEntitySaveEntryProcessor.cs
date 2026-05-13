// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;
using Headless.Abstractions;
using Headless.Domain;
using Headless.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Options;

namespace Headless.EntityFramework.Contexts.Processors;

[PublicAPI]
public sealed class HeadlessEntitySaveEntryProcessor(
    IGuidGenerator guidGenerator,
    IOptions<TenantWriteGuardOptions> tenantWriteGuardOptions,
    ITenantWriteGuardBypass tenantWriteGuardBypass
) : IHeadlessSaveEntryProcessor
{
    private static readonly ConditionalWeakTable<Type, StrongBox<bool>> _ShouldStampGuidIdCache = new();

    private static readonly ConditionalWeakTable<Type, StrongBox<bool>>.CreateValueCallback _ShouldStampGuidIdFactory =
        static type =>
        {
            var idProperty = type.GetProperty(nameof(IEntity<>.Id));

            if (idProperty is null)
            {
                return new StrongBox<bool>(value: false);
            }

            var stamp =
                idProperty.GetFirstOrDefaultAttribute<DatabaseGeneratedAttribute>()
                is not { DatabaseGeneratedOption: not DatabaseGeneratedOption.None };

            return new StrongBox<bool>(stamp);
        };

    public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
    {
        _EnsureTenantWriteAllowed(entry, context.TenantId);

        switch (entry.State)
        {
            case EntityState.Added:
                _TrySetGuidId(entry);
                _TrySetMultiTenantId(entry, context.TenantId);
                _TrySetConcurrencyStamp(entry);
                break;
            case EntityState.Modified:
                _TrySetConcurrencyStamp(entry);
                break;
        }
    }

    private void _EnsureTenantWriteAllowed(EntityEntry entry, string? tenantId)
    {
        if (
            entry.Entity is not IMultiTenant entity
            || entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)
            || !tenantWriteGuardOptions.Value.IsEnabled
            || tenantWriteGuardBypass.IsActive
        )
        {
            return;
        }

        var currentTenantId = _NormalizeTenantId(tenantId);
        var entityTenantId = _NormalizeTenantId(entry.Property(nameof(IMultiTenant.TenantId)).CurrentValue);

        if (currentTenantId is null)
        {
            throw new MissingTenantContextException(
                $"Tenant-owned {entry.State} write for entity type '{_GetEntityTypeName(entry)}' requires an ambient "
                    + "tenant context. Use ICurrentTenant.Change(tenantId) to scope the operation, or "
                    + "ITenantWriteGuardBypass.BeginBypass() for intentional host/admin writes."
            );
        }

        if (entry.State == EntityState.Added && entityTenantId is null)
        {
            // The guard contract requires callers to stamp TenantId at construction (or via the
            // _TrySetMultiTenantId post-processor below). A null TenantId on an Added IMultiTenant
            // entry under guard would otherwise rely on the ambient ICurrentTenant being copied at
            // SaveChanges time, which silently couples write correctness to whatever tenant the
            // current scope happens to observe — a race vector documented as a known gap. Reject
            // the write so the failure is loud at the call site.
            throw new MissingTenantContextException(
                $"Tenant-owned Added write for entity type '{_GetEntityTypeName(entry)}' has a null TenantId. "
                    + "Stamp the TenantId at construction (or use ITenantWriteGuardBypass.BeginBypass() for "
                    + "intentional host/admin writes); the guard no longer back-stamps the ambient tenant on "
                    + "Added entries."
            );
        }

        if (_TenantWriteMatches(entry, currentTenantId, entityTenantId))
        {
            return;
        }

        throw new CrossTenantWriteException(
            _GetEntityTypeName(entry),
            entry.State.ToString(),
            currentTenantAvailable: true,
            entityTenantAvailable: entityTenantId is not null,
            tenantMatches: false
        );
    }

    private static bool _TenantWriteMatches(EntityEntry entry, string currentTenantId, string? entityTenantId)
    {
        if (!string.Equals(entityTenantId, currentTenantId, StringComparison.Ordinal))
        {
            return false;
        }

        // For Modified and Deleted states, also verify the OriginalValue (the loaded-from-database
        // tenant) matches the current tenant. This blocks Attach + rewrite + Remove patterns where
        // the attacker controls CurrentValue but OriginalValue reflects another tenant's row.
        if (entry.State is not (EntityState.Modified or EntityState.Deleted))
        {
            return true;
        }

        var originalTenantId = _NormalizeTenantId(entry.Property(nameof(IMultiTenant.TenantId)).OriginalValue);
        return string.Equals(originalTenantId, currentTenantId, StringComparison.Ordinal);
    }

    private static string? _NormalizeTenantId(object? value)
    {
        return value is string tenantId && !string.IsNullOrWhiteSpace(tenantId) ? tenantId : null;
    }

    private static string _GetEntityTypeName(EntityEntry entry)
    {
        return entry.Metadata.ClrType.FullName ?? entry.Metadata.ClrType.Name;
    }

    private void _TrySetGuidId(EntityEntry entry)
    {
        if (entry.Entity is not IEntity<Guid> entity || entity.Id != Guid.Empty)
        {
            return;
        }

        if (!_ShouldStampGuidId(entity.GetType()))
        {
            return;
        }

        ObjectPropertiesHelper.TrySetProperty(entity, x => x.Id, guidGenerator.Create);
    }

    private static bool _ShouldStampGuidId(Type entityType)
    {
        return _ShouldStampGuidIdCache.GetValue(entityType, _ShouldStampGuidIdFactory).Value;
    }

    private static void _TrySetMultiTenantId(EntityEntry entry, string? tenantId)
    {
        if (entry.Entity is not IMultiTenant entity || !string.IsNullOrEmpty(entity.TenantId))
        {
            return;
        }

        if (entry.Property(nameof(IMultiTenant.TenantId)) is { IsModified: true, CurrentValue: not (null or "") })
        {
            return;
        }

        ObjectPropertiesHelper.TrySetProperty(entity, x => x.TenantId, () => tenantId);
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
