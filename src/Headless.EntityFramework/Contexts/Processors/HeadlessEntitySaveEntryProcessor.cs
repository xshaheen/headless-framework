// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Domain;
using Headless.EntityFramework.Contexts.Runtime;
using Headless.MultiTenancy;
using Headless.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;

namespace Headless.EntityFramework.Contexts.Processors;

/// <summary>
/// Save-entry processor that enforces tenant write isolation and stamps infrastructure fields
/// (tenant identifier and concurrency stamp) on entities before they reach the database.
/// </summary>
/// <remarks>
/// When the tenant write guard is enabled this processor rejects writes that lack an ambient tenant
/// context (<c>MissingTenantContextException</c>) and writes whose entity <c>TenantId</c> does not
/// match the current tenant's identifier (<c>CrossTenantWriteException</c>). Original tracked values
/// are also checked; crafted detached snapshots are fenced by SQL concurrency predicates. Use
/// <c>ITenantWriteGuardBypass.BeginBypass()</c> for intentional host or admin writes that must
/// operate across tenants.
/// </remarks>
[PublicAPI]
public sealed class HeadlessEntitySaveEntryProcessor(
    IOptions<TenantWriteGuardOptions> tenantWriteGuardOptions,
    ITenantWriteGuardBypass tenantWriteGuardBypass
) : IHeadlessSaveEntryProcessor
{
    /// <summary>
    /// Enforces tenant write isolation and stamps the tenant identifier and concurrency stamp on the
    /// entry.
    /// </summary>
    /// <param name="entry">The tracked entity entry to process.</param>
    /// <param name="context">The per-save scratchpad carrying the ambient tenant identifier.</param>
    public void Process(EntityEntry entry, HeadlessSaveEntryContext context)
    {
        _EnsureTenantWriteAllowed(entry, context);

        switch (entry.State)
        {
            case EntityState.Added:
                // Guid keys are produced by the EF Core value generator (ConfigureHeadlessValueGenerated) when the
                // entity transitions to Added, so by the time it reaches the save pipeline the id is already set.
                if (!tenantWriteGuardOptions.Value.IsEnabled || tenantWriteGuardBypass.IsActive)
                {
                    _TrySetMultiTenantId(entry, context.TenantId);
                }
                _TrySetConcurrencyStamp(entry);
                break;
            case EntityState.Modified:
                _TrySetConcurrencyStamp(entry);
                break;
        }
    }

    private void _EnsureTenantWriteAllowed(EntityEntry entry, HeadlessSaveEntryContext context)
    {
        if (
            !entry.Metadata.IsTenantOwned()
            || entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)
        )
        {
            return;
        }

        var guarded = tenantWriteGuardOptions.Value.IsEnabled && !tenantWriteGuardBypass.IsActive;
        var currentTenantId = _ReadTenantId(context.TenantId);
        if (!guarded && entry.Metadata.IsOwned())
        {
            return;
        }

        var root = entry.Metadata.IsOwned() ? _ResolveOwner(entry, context.DbContext) : entry;
        var tenantProperty = root.Property(root.Metadata.GetTenantPropertyName()!);
        var entityTenantId = _ReadTenantId(tenantProperty.CurrentValue);
        var originalTenantId = root.State == EntityState.Added ? null : _ReadTenantId(tenantProperty.OriginalValue);
        if (!guarded)
        {
            return;
        }

        if (currentTenantId is null)
        {
            throw new MissingTenantContextException(
                $"Tenant-owned {entry.State} write for entity type '{_GetEntityTypeName(entry)}' requires an ambient "
                    + "tenant context. Use ICurrentTenant.Change(tenantId) to scope the operation, or "
                    + "ITenantWriteGuardBypass.BeginBypass() for intentional host/admin writes."
            );
        }

        if (root.State == EntityState.Added && entityTenantId is null)
        {
            throw new MissingTenantContextException(
                $"Tenant-owned Added write for entity type '{_GetEntityTypeName(entry)}' has no tenant. "
                    + "The tenant must be captured before tracking; SaveChanges does not stamp guarded entries."
            );
        }

        if (
            string.Equals(entityTenantId, currentTenantId, StringComparison.Ordinal)
            && (
                root.State == EntityState.Added
                || string.Equals(originalTenantId, currentTenantId, StringComparison.Ordinal)
            )
        )
        {
            return;
        }

        throw new CrossTenantWriteException(_GetEntityTypeName(entry), entry.State.ToString());
    }

    private static EntityEntry _ResolveOwner(EntityEntry entry, DbContext db)
    {
        // Navigation references can be stale or caller-supplied; only tracked ownership keys identify the row owner.
        var tracked = db.ChangeTracker.Entries().ToArray();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var current = entry;
        while (current.Metadata.FindOwnership() is { } ownership)
        {
            if (!visited.Add(current.Entity))
            {
                throw new CrossTenantWriteException(_GetEntityTypeName(entry), entry.State.ToString());
            }

            var principal = _FindPrincipal(current, ownership, tracked, original: false);
            if (
                principal is null
                || current.State != EntityState.Added
                    && _FindPrincipal(current, ownership, tracked, original: true)?.Entity != principal.Entity
            )
            {
                throw new CrossTenantWriteException(_GetEntityTypeName(entry), entry.State.ToString());
            }

            current = principal;
        }

        return current;
    }

    private static EntityEntry? _FindPrincipal(
        EntityEntry dependent,
        IForeignKey ownership,
        EntityEntry[] tracked,
        bool original
    )
    {
        EntityEntry? match = null;
        foreach (var candidate in tracked)
        {
            if (!ownership.PrincipalEntityType.IsAssignableFrom(candidate.Metadata))
            {
                continue;
            }

            var matches = true;
            for (var i = 0; i < ownership.Properties.Count; i++)
            {
                var foreignKey = (original ? dependent.OriginalValues : dependent.CurrentValues)[
                    ownership.Properties[i]
                ];
                var keyProperty = ownership.PrincipalKey.Properties[i];
                var principalKey = (original ? candidate.OriginalValues : candidate.CurrentValues)[keyProperty];
                var comparer = keyProperty.GetKeyValueComparer();
                if (
                    foreignKey is null
                    || principalKey is null
                    || !comparer.Equals(foreignKey, principalKey)
                    || dependent.State != EntityState.Added
                        && !comparer.Equals(
                            dependent.OriginalValues[ownership.Properties[i]],
                            dependent.CurrentValues[ownership.Properties[i]]
                        )
                    || candidate.State != EntityState.Added
                        && !comparer.Equals(candidate.OriginalValues[keyProperty], candidate.CurrentValues[keyProperty])
                )
                {
                    matches = false;
                    break;
                }
            }

            if (!matches)
            {
                continue;
            }

            if (match is not null)
            {
                return null;
            }

            match = candidate;
        }

        return match;
    }

    private static string? _ReadTenantId(object? value)
    {
        var tenantId = HeadlessTenantModelConvention.ValidateTenantId((string?)value);
        return string.IsNullOrWhiteSpace(tenantId) ? null : tenantId;
    }

    private static string _GetEntityTypeName(EntityEntry entry)
    {
        return entry.Metadata.ClrType.FullName ?? entry.Metadata.ClrType.Name;
    }

    private static void _TrySetMultiTenantId(EntityEntry entry, string? tenantId)
    {
        if (
            !entry.Metadata.IsTenantOwned()
            || entry.Entity is not IMultiTenant entity
            || !string.IsNullOrEmpty(entity.TenantId)
        )
        {
            return;
        }

        if (entry.Property(nameof(IMultiTenant.TenantId)) is { IsModified: true, CurrentValue: not (null or "") })
        {
            return;
        }

        ObjectPropertiesHelper.TrySetPropertyValue(entity, nameof(IMultiTenant.TenantId), tenantId);
    }

    private static void _TrySetConcurrencyStamp(EntityEntry entry)
    {
        if (entry.State == EntityState.Added)
        {
            if (entry.Entity is IHasConcurrencyStamp { ConcurrencyStamp: null } added)
            {
                ObjectPropertiesHelper.TrySetProperty(
                    added,
                    nameof(IHasConcurrencyStamp.ConcurrencyStamp),
                    () => Guid.NewGuid().ToString("N")
                );
            }

            return;
        }

        if (entry.Entity is IHasConcurrencyStamp modified)
        {
            ObjectPropertiesHelper.TrySetProperty(
                modified,
                nameof(IHasConcurrencyStamp.ConcurrencyStamp),
                () => Guid.NewGuid().ToString("N")
            );
        }
    }
}
