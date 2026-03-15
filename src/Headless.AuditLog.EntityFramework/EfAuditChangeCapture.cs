// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog;

internal sealed class EfAuditChangeCapture(IOptions<AuditLogOptions> options, ILogger<EfAuditChangeCapture> logger)
    : IAuditChangeCapture
{
    private static readonly ConcurrentDictionary<PropertyInfo, AuditPropertyMetadata> _PropertyCache = new();

    private static readonly HashSet<string> _DefaultExcludedProperties = new(StringComparer.Ordinal)
    {
        "ConcurrencyStamp",
        "DateCreated",
        "DateUpdated",
        "DateDeleted",
        "DateSuspended",
        "CreatedById",
        "UpdatedById",
        "DeletedById",
        "SuspendedById",
    };

    /// <inheritdoc />
    public IReadOnlyList<AuditLogEntryData> CaptureChanges(
        IEnumerable<object> entries,
        string? userId,
        string? accountId,
        string? tenantId,
        string? correlationId,
        DateTimeOffset timestamp
    )
    {
        var opts = options.Value;
        if (!opts.IsEnabled)
            return [];

        var result = new List<AuditLogEntryData>();

        foreach (var obj in entries)
        {
            if (obj is not EntityEntry entry)
                continue;

            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            try
            {
                if (!_ShouldAudit(entry, opts))
                    continue;

                var data = _CaptureEntry(entry, opts, userId, accountId, tenantId, correlationId, timestamp);

                if (data is not null)
                    result.Add(data);
            }
            catch (Exception ex)
            {
                if (ex is OptionsValidationException)
                    throw;

                logger.LogWarning(
                    ex,
                    "Audit capture failed for entity {EntityType}. Audit entry skipped; entity save continues.",
                    entry.Metadata.ClrType.FullName
                );
            }
        }

        return result;
    }

    private static bool _ShouldAudit(EntityEntry entry, AuditLogOptions opts)
    {
        var clrType = entry.Metadata.ClrType;

        // Owned entities inherit auditability from their owner
        if (entry.Metadata.IsOwned())
        {
            var ownerType = entry.Metadata.FindOwnership()!.PrincipalEntityType.ClrType;
            return _IsAuditable(ownerType, opts);
        }

        return _IsAuditable(clrType, opts);
    }

    private static bool _IsAuditable(Type clrType, AuditLogOptions opts)
    {
        if (opts.AuditAllEntities)
        {
            if (clrType.GetCustomAttribute<AuditIgnoreAttribute>() is not null)
                return false;
        }
        else
        {
            if (!typeof(IAuditTracked).IsAssignableFrom(clrType))
                return false;
        }

        return opts.EntityFilter?.Invoke(clrType) != true;
    }

    private static AuditLogEntryData? _CaptureEntry(
        EntityEntry entry,
        AuditLogOptions opts,
        string? userId,
        string? accountId,
        string? tenantId,
        string? correlationId,
        DateTimeOffset timestamp
    )
    {
        var clrType = entry.Metadata.ClrType;

        AuditChangeType? changeType = entry.State switch
        {
            EntityState.Added => AuditChangeType.Created,
            EntityState.Modified => AuditChangeType.Updated,
            EntityState.Deleted => AuditChangeType.Deleted,
            _ => null,
        };

        if (changeType is null)
            return null;

        var action = _DetermineAction(entry, changeType.Value);

        var oldValues = new Dictionary<string, object?>(StringComparer.Ordinal);
        var newValues = new Dictionary<string, object?>(StringComparer.Ordinal);
        var changedFields = new List<string>();

        foreach (var property in entry.Properties)
        {
            if (property.Metadata.PropertyInfo is null)
                continue; // shadow properties — skip

            var propertyName = property.Metadata.Name;

            // Default framework property exclusion
            if (_DefaultExcludedProperties.Contains(propertyName))
                continue;

            var meta = _GetPropertyMetadata(property.Metadata.PropertyInfo);

            // [AuditIgnore] — skip entirely
            if (meta.IsIgnored)
                continue;

            // Option-based property filter
            if (opts.PropertyFilter?.Invoke(clrType, propertyName) == true)
                continue;

            // [AuditSensitive] — apply strategy
            if (meta.IsSensitive)
            {
                var strategy = meta.SensitiveStrategy ?? opts.SensitiveDataStrategy;
                _ApplySensitiveValues(
                    strategy,
                    entry,
                    property,
                    opts,
                    clrType,
                    propertyName,
                    oldValues,
                    newValues,
                    changedFields
                );
                continue;
            }

            // Normal property
            _ApplyValues(
                changeType.Value,
                oldValues,
                newValues,
                changedFields,
                propertyName,
                property.OriginalValue,
                property.CurrentValue,
                property.IsModified
            );
        }

        // Skip updates with no real changed fields
        if (changeType == AuditChangeType.Updated && changedFields.Count == 0)
            return null;

        var (entityType, entityId) = _GetEntityIdentity(entry);

        return new AuditLogEntryData
        {
            UserId = userId,
            AccountId = accountId,
            TenantId = tenantId,
            CorrelationId = correlationId,
            Action = action,
            ChangeType = changeType,
            EntityType = entityType,
            EntityId = entityId,
            OldValues = changeType == AuditChangeType.Created ? null : (oldValues.Count > 0 ? oldValues : null),
            NewValues = changeType == AuditChangeType.Deleted ? null : (newValues.Count > 0 ? newValues : null),
            ChangedFields = changedFields.Count > 0 ? changedFields : null,
            CreatedAt = timestamp,
        };
    }

    private static string _DetermineAction(EntityEntry entry, AuditChangeType changeType)
    {
        if (changeType == AuditChangeType.Updated)
        {
            // Soft-delete detection: check IsDeleted transition
            var isDeletedProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "IsDeleted");

            if (isDeletedProp is not null && isDeletedProp.IsModified)
            {
                var nowDeleted = isDeletedProp.CurrentValue is true;
                var wasDeleted = isDeletedProp.OriginalValue is true;

                if (!wasDeleted && nowDeleted)
                    return "entity.soft_deleted";
                if (wasDeleted && !nowDeleted)
                    return "entity.restored";
            }

            // Suspend detection: check IsSuspended transition
            var isSuspendedProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "IsSuspended");

            if (isSuspendedProp is not null && isSuspendedProp.IsModified)
            {
                var nowSuspended = isSuspendedProp.CurrentValue is true;
                var wasSuspended = isSuspendedProp.OriginalValue is true;

                if (!wasSuspended && nowSuspended)
                    return "entity.suspended";
                if (wasSuspended && !nowSuspended)
                    return "entity.unsuspended";
            }
        }

        return changeType switch
        {
            AuditChangeType.Created => "entity.created",
            AuditChangeType.Updated => "entity.updated",
            AuditChangeType.Deleted => "entity.deleted",
            _ => "entity.unknown",
        };
    }

    private static void _ApplySensitiveValues(
        SensitiveDataStrategy strategy,
        EntityEntry entry,
        PropertyEntry property,
        AuditLogOptions opts,
        Type clrType,
        string propertyName,
        Dictionary<string, object?> oldValues,
        Dictionary<string, object?> newValues,
        List<string> changedFields
    )
    {
        AuditChangeType changeType = entry.State switch
        {
            EntityState.Added => AuditChangeType.Created,
            EntityState.Modified => AuditChangeType.Updated,
            EntityState.Deleted => AuditChangeType.Deleted,
            _ => AuditChangeType.Updated,
        };

        switch (strategy)
        {
            case SensitiveDataStrategy.Exclude:
                return;

            case SensitiveDataStrategy.Redact:
                _ApplyValues(
                    changeType,
                    oldValues,
                    newValues,
                    changedFields,
                    propertyName,
                    "***",
                    "***",
                    property.IsModified
                );
                break;

            case SensitiveDataStrategy.Transform:
                var transformer =
                    opts.SensitiveValueTransformer
                    ?? throw new OptionsValidationException(
                        nameof(AuditLogOptions),
                        typeof(AuditLogOptions),
                        [
                            "SensitiveValueTransformer must be configured when SensitiveDataStrategy is Transform."
                        ]
                    );

                object? transformedNew = null;
                object? transformedOld = null;

                try
                {
                    transformedNew = transformer(
                        new SensitiveValueContext(
                            clrType.FullName ?? clrType.Name,
                            propertyName,
                            property.Metadata.ClrType,
                            property.CurrentValue
                        )
                    );
                    transformedOld = transformer(
                        new SensitiveValueContext(
                            clrType.FullName ?? clrType.Name,
                            propertyName,
                            property.Metadata.ClrType,
                            property.OriginalValue
                        )
                    );
                }
                catch (Exception ex) when (_FallbackToRedact(ex))
                {
                    // Transformer threw — fall back to Redact for this property
                    _ApplyValues(
                        changeType,
                        oldValues,
                        newValues,
                        changedFields,
                        propertyName,
                        "***",
                        "***",
                        property.IsModified
                    );
                    break;
                }

                _ApplyValues(
                    changeType,
                    oldValues,
                    newValues,
                    changedFields,
                    propertyName,
                    transformedOld,
                    transformedNew,
                    property.IsModified
                );
                break;
        }
    }

    private static void _ApplyValues(
        AuditChangeType changeType,
        Dictionary<string, object?> oldValues,
        Dictionary<string, object?> newValues,
        List<string> changedFields,
        string propertyName,
        object? oldValue,
        object? newValue,
        bool isModified
    )
    {
        switch (changeType)
        {
            case AuditChangeType.Created:
                newValues[propertyName] = newValue;
                break;

            case AuditChangeType.Deleted:
                oldValues[propertyName] = oldValue;
                break;

            case AuditChangeType.Updated when isModified:
                oldValues[propertyName] = oldValue;
                newValues[propertyName] = newValue;
                changedFields.Add(propertyName);
                break;
        }
    }

    private static (string? entityType, string? entityId) _GetEntityIdentity(EntityEntry entry)
    {
        if (entry.Metadata.IsOwned())
        {
            var ownership = entry.Metadata.FindOwnership()!;
            var ownerType = ownership.PrincipalEntityType.ClrType;
            var ownedType = entry.Metadata.ClrType;

            var entityType = $"{ownerType.FullName}.{ownedType.Name}";

            var ownerKeyValues = ownership.Properties.Select(p => entry.Property(p.Name).CurrentValue).ToArray();

            var entityId =
                ownerKeyValues.Length == 1
                    ? ownerKeyValues[0]?.ToString()
                    : string.Join(",", ownerKeyValues.Select(v => v?.ToString()));

            return (entityType, entityId);
        }

        var key = entry.Metadata.FindPrimaryKey();

        if (key is null)
            return (entry.Metadata.ClrType.FullName, null);

        var values = key.Properties.Select(p => entry.Property(p.Name).CurrentValue).ToArray();

        var id = values.Length == 1 ? values[0]?.ToString() : string.Join(",", values.Select(v => v?.ToString()));

        return (entry.Metadata.ClrType.FullName, id);
    }

    // Returns true so the exception filter always matches — the exception is intentionally
    // swallowed here; we fall back to Redact rather than propagating transformer errors.
    private static bool _FallbackToRedact(Exception _) => true;

    private static AuditPropertyMetadata _GetPropertyMetadata(PropertyInfo propInfo) =>
        _PropertyCache.GetOrAdd(
            propInfo,
            static pi =>
            {
                var ignore = pi.GetCustomAttribute<AuditIgnoreAttribute>();
                var sensitive = pi.GetCustomAttribute<AuditSensitiveAttribute>();

                return new(
                    IsIgnored: ignore is not null,
                    IsSensitive: sensitive is not null,
                    SensitiveStrategy: sensitive?.Strategy
                );
            }
        );

    private sealed record AuditPropertyMetadata(
        bool IsIgnored,
        bool IsSensitive,
        SensitiveDataStrategy? SensitiveStrategy
    );
}
