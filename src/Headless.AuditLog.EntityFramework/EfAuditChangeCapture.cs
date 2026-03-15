// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog;

internal sealed class EfAuditChangeCapture(IOptions<AuditLogOptions> options, ILogger<EfAuditChangeCapture> logger)
    : IAuditChangeCapture, IAuditEntityIdResolver
{
    private static readonly ConcurrentDictionary<PropertyInfo, AuditPropertyMetadata> _PropertyCache = new();
    private readonly ConcurrentDictionary<Type, bool> _entityFilterCache = new();
    private readonly ConcurrentDictionary<(Type Type, string PropertyName), bool> _propertyFilterCache = new();
    private readonly List<(AuditLogEntryData Data, EntityEntry EntityEntry)> _deferredEntityIds = [];
    private bool _hasLoggedDisabledWarning;

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
        {
            _LogDisabledWarningOnce();
            return [];
        }

        _deferredEntityIds.Clear();
        List<AuditLogEntryData>? result = null;

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
                {
                    result ??= [];
                    result.Add(data);

                    // Defer EntityId resolution for Added entities with store-generated keys.
                    if (entry.State == EntityState.Added)
                        _deferredEntityIds.Add((data, entry));
                }
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

        return result ?? [];
    }

    /// <inheritdoc />
    public void ResolveEntityIds(IReadOnlyList<AuditLogEntryData> entries)
    {
        foreach (var (data, entityEntry) in _deferredEntityIds)
        {
            var (_, entityId) = _GetEntityIdentity(entityEntry);
            data.EntityId = entityId;
        }

        // Not cleared here — cleared at the start of the next CaptureChanges call.
        // This allows execution strategy retries to re-resolve after failed attempts
        // where store-generated keys may differ.
    }

    private void _LogDisabledWarningOnce()
    {
        if (_hasLoggedDisabledWarning)
            return;

        _hasLoggedDisabledWarning = true;
        logger.LogWarning(
            "Audit logging is disabled. Set AuditLogOptions.IsEnabled = true to enable audit capture."
        );
    }

    private bool _ShouldAudit(EntityEntry entry, AuditLogOptions opts)
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

    private bool _IsAuditable(Type clrType, AuditLogOptions opts)
    {
        if (opts.AuditByDefault)
        {
            if (clrType.GetCustomAttribute<AuditIgnoreAttribute>() is not null)
                return false;
        }
        else
        {
            if (!typeof(IAuditTracked).IsAssignableFrom(clrType))
                return false;
        }

        return !_ShouldExcludeEntity(clrType, opts);
    }

    private bool _ShouldExcludeEntity(Type clrType, AuditLogOptions opts)
    {
        if (opts.EntityFilter is null)
            return false;

        return _entityFilterCache.GetOrAdd(clrType, static (type, filter) => filter(type), opts.EntityFilter);
    }

    private bool _ShouldExcludeProperty(Type clrType, string propertyName, AuditLogOptions opts)
    {
        if (opts.PropertyFilter is null)
            return false;

        return _propertyFilterCache.GetOrAdd(
            (clrType, propertyName),
            static (key, filter) => filter(key.Type, key.PropertyName),
            opts.PropertyFilter
        );
    }

    private AuditLogEntryData? _CaptureEntry(
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

        var oldValues = new Dictionary<string, object?>(StringComparer.Ordinal);
        var newValues = new Dictionary<string, object?>(StringComparer.Ordinal);
        var changedFields = new List<string>();
        var actionContext = new ActionContext();

        foreach (var property in entry.Properties)
        {
            if (property.Metadata.PropertyInfo is null)
                continue; // shadow properties — skip

            var propertyName = property.Metadata.Name;

            // Default framework property exclusion
            if (opts.DefaultExcludedProperties.Contains(propertyName))
                continue;

            var meta = _GetPropertyMetadata(property.Metadata.PropertyInfo);

            // [AuditIgnore] — skip entirely
            if (meta.IsIgnored)
                continue;

            // Option-based property filter
            if (_ShouldExcludeProperty(clrType, propertyName, opts))
                continue;

            _CaptureActionFlags(actionContext, propertyName, property);

            // [AuditSensitive] — apply strategy
            if (meta.IsSensitive)
            {
                var strategy = meta.SensitiveStrategy ?? opts.SensitiveDataStrategy;
                _ApplySensitiveValues(
                    changeType.Value,
                    strategy,
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

        var action = _DetermineAction(changeType.Value, actionContext);
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

    private static void _CaptureActionFlags(ActionContext context, string propertyName, PropertyEntry property)
    {
        if (!property.IsModified)
            return;

        if (propertyName == "IsDeleted")
        {
            var nowDeleted = property.CurrentValue is true;
            var wasDeleted = property.OriginalValue is true;

            context.IsSoftDeleted = !wasDeleted && nowDeleted;
            context.IsRestored = wasDeleted && !nowDeleted;
            return;
        }

        if (propertyName == "IsSuspended")
        {
            var nowSuspended = property.CurrentValue is true;
            var wasSuspended = property.OriginalValue is true;

            context.IsSuspended = !wasSuspended && nowSuspended;
            context.IsUnsuspended = wasSuspended && !nowSuspended;
        }
    }

    private static string _DetermineAction(AuditChangeType changeType, ActionContext context)
    {
        if (changeType == AuditChangeType.Updated)
        {
            if (context.IsSoftDeleted)
                return AuditActionNames.SoftDeleted;
            if (context.IsRestored)
                return AuditActionNames.Restored;
            if (context.IsSuspended)
                return AuditActionNames.Suspended;
            if (context.IsUnsuspended)
                return AuditActionNames.Unsuspended;
        }

        return changeType switch
        {
            AuditChangeType.Created => AuditActionNames.Created,
            AuditChangeType.Updated => AuditActionNames.Updated,
            AuditChangeType.Deleted => AuditActionNames.Deleted,
            _ => AuditActionNames.Unknown,
        };
    }

    private void _ApplySensitiveValues(
        AuditChangeType changeType,
        SensitiveDataStrategy strategy,
        PropertyEntry property,
        AuditLogOptions opts,
        Type clrType,
        string propertyName,
        Dictionary<string, object?> oldValues,
        Dictionary<string, object?> newValues,
        List<string> changedFields
    )
    {
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

                try
                {
                    var transformedNew = transformer(
                        new SensitiveValueContext(
                            clrType.FullName ?? clrType.Name,
                            propertyName,
                            property.Metadata.ClrType,
                            property.CurrentValue
                        )
                    );
                    var transformedOld = transformer(
                        new SensitiveValueContext(
                            clrType.FullName ?? clrType.Name,
                            propertyName,
                            property.Metadata.ClrType,
                            property.OriginalValue
                        )
                    );

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
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Sensitive value transformer threw for {EntityType}.{PropertyName}. Falling back to Redact.",
                        clrType.FullName ?? clrType.Name,
                        propertyName
                    );

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
                }
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
            return (entityType, _FormatEntityId(ownerKeyValues));
        }

        var key = entry.Metadata.FindPrimaryKey();

        if (key is null)
            return (entry.Metadata.ClrType.FullName, null);

        var values = key.Properties.Select(p => entry.Property(p.Name).CurrentValue).ToArray();

        return (entry.Metadata.ClrType.FullName, _FormatEntityId(values));
    }

    private static string? _FormatEntityId(object?[] values)
    {
        if (values.Length == 0)
            return null;

        if (values.Length == 1)
            return values[0]?.ToString();

        return JsonSerializer.Serialize(values.Select(static value => value?.ToString()).ToArray());
    }

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

    private sealed class ActionContext
    {
        public bool IsSoftDeleted { get; set; }

        public bool IsRestored { get; set; }

        public bool IsSuspended { get; set; }

        public bool IsUnsuspended { get; set; }
    }
}

internal static class AuditActionNames
{
    public const string SoftDeleted = "entity.soft_deleted";
    public const string Restored = "entity.restored";
    public const string Suspended = "entity.suspended";
    public const string Unsuspended = "entity.unsuspended";
    public const string Created = "entity.created";
    public const string Updated = "entity.updated";
    public const string Deleted = "entity.deleted";
    public const string Unknown = "entity.unknown";
}
