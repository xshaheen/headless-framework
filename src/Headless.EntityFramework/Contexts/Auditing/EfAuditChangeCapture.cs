// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Headless.AuditLog;
using Headless.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

internal sealed class EfAuditChangeCapture(
    IOptions<AuditLogOptions> options,
    ILogger<EfAuditChangeCapture>? logger = null
) : IAuditChangeCapture, IAuditEntityIdResolver
{
    // Optional logger at parity with the sibling save-pipeline services (HeadlessAuditPersistence,
    // HeadlessSaveChangesPipeline): callers wiring this capture by hand — tests, minimal hosts — need not
    // register ILogger<T>. Defaults to NullLogger rather than forcing an AddLogging() dependency.
    private readonly ILogger<EfAuditChangeCapture> _logger = logger ?? NullLogger<EfAuditChangeCapture>.Instance;

    private static readonly ConditionalWeakTable<
        Type,
        ConcurrentDictionary<string, AuditPropertyMetadata>
    > _PropertyCache = [];

    private static readonly ConditionalWeakTable<
        Type,
        ConcurrentDictionary<string, AuditPropertyMetadata>
    >.CreateValueCallback _CreatePropertyInner = static _ => new ConcurrentDictionary<string, AuditPropertyMetadata>(
        StringComparer.Ordinal
    );

    // Static cache because the result is a pure function of (Type, AuditByDefault).
    // Survives across scoped capture instances and across requests.
    private static readonly ConcurrentDictionary<(Type Type, bool AuditByDefault), bool> _IsAuditableCache = new();

    private readonly ConcurrentDictionary<Type, bool> _entityFilterCache = new();
    private readonly ConcurrentDictionary<(Type Type, string PropertyName), bool> _propertyFilterCache = new();

    // Deferred store-generated value resolution, keyed on the produced data object itself so
    // interleaved captures on the same scoped instance (e.g. a domain-event handler saving a
    // second DbContext mid-save) cannot clobber each other's state. Entries intentionally stay
    // registered after resolution so execution-strategy retries can re-resolve; the table
    // releases them together with their data objects.
    private readonly ConditionalWeakTable<AuditLogEntryData, DeferredEntryResolution> _deferredResolutions = [];

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

        List<AuditLogEntryData>? result = null;

        foreach (var obj in entries)
        {
            if (obj is not EntityEntry entry)
            {
                continue;
            }

            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            try
            {
                if (!_ShouldAudit(entry, opts))
                {
                    continue;
                }

                var data = _CaptureEntry(entry, opts, userId, accountId, tenantId, correlationId, timestamp);

                if (data is not null)
                {
                    result ??= [];
                    result.Add(data);
                }
            }
            catch (Exception e)
                when (e is not OptionsValidationException && opts.CaptureErrorStrategy == CaptureErrorStrategy.Continue)
            {
                // Continue-only: under CaptureErrorStrategy.Throw the failure propagates so the
                // save aborts instead of committing an entity change without its audit row.
                _logger.LogAuditCaptureFailed(e, entry.Metadata.ClrType.FullName);
            }
        }

        return result ?? [];
    }

    /// <inheritdoc />
    public void ResolveEntityIds(IReadOnlyList<AuditLogEntryData> entries)
    {
        foreach (var data in entries)
        {
            if (!_deferredResolutions.TryGetValue(data, out var resolution))
            {
                continue;
            }

            if (resolution.ResolveEntityId)
            {
                var (_, entityId) = _GetEntityIdentity(resolution.Entry);
                data.EntityId = entityId;
            }

            if (resolution.TemporaryValuePatches is not null)
            {
                foreach (var patch in resolution.TemporaryValuePatches)
                {
                    patch.Target[patch.PropertyName] = patch.Property.CurrentValue;
                }
            }
        }
    }

    private void _LogDisabledWarningOnce()
    {
        if (_hasLoggedDisabledWarning)
        {
            return;
        }

        _hasLoggedDisabledWarning = true;
        _logger.LogAuditDisabled();
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
        // The attribute/interface gate is a pure function of (Type, AuditByDefault) — cache it
        // process-wide. The opts-instance-bound filter is cached separately on the instance.
        if (!_IsAuditableByAttributes(clrType, opts.AuditByDefault))
        {
            return false;
        }

        return !_ShouldExcludeEntity(clrType, opts);
    }

    private static bool _IsAuditableByAttributes(Type clrType, bool auditByDefault)
    {
        return _IsAuditableCache.GetOrAdd(
            (clrType, auditByDefault),
            static key =>
            {
                var (type, byDefault) = key;

                if (byDefault)
                {
                    return !Attribute.IsDefined(type, typeof(AuditIgnoreAttribute));
                }

                return typeof(IAuditTracked).IsAssignableFrom(type);
            }
        );
    }

    private bool _ShouldExcludeEntity(Type clrType, AuditLogOptions opts)
    {
        if (opts.EntityFilter is null)
        {
            return false;
        }

        return _entityFilterCache.GetOrAdd(clrType, static (type, filter) => filter(type), opts.EntityFilter);
    }

    private bool _ShouldExcludeProperty(Type clrType, string propertyName, AuditLogOptions opts)
    {
        if (opts.PropertyFilter is null)
        {
            return false;
        }

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
        {
            return null;
        }

        var oldValues = new Dictionary<string, object?>(StringComparer.Ordinal);
        var newValues = new Dictionary<string, object?>(StringComparer.Ordinal);
        var changedFields = new List<string>();
        var actionContext = new ActionContext();
        List<TemporaryValuePatch>? temporaryValuePatches = null;

        foreach (var property in entry.Properties)
        {
            if (property.Metadata.PropertyInfo is null)
            {
                continue; // shadow properties — skip
            }

            var propertyName = property.Metadata.Name;

            // Default framework property exclusion
            if (opts.DefaultExcludedProperties.Contains(propertyName))
            {
                continue;
            }

            var meta = _GetPropertyMetadata(property.Metadata.PropertyInfo);

            // [AuditIgnore] — skip entirely
            if (meta.IsIgnored)
            {
                continue;
            }

            // Option-based property filter
            if (_ShouldExcludeProperty(clrType, propertyName, opts))
            {
                continue;
            }

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

            // Store-generated keys and FKs pointing at just-added principals hold EF temporary
            // values until SaveChanges runs; remember where the value landed so ResolveEntityIds
            // can patch in the real value post-save.
            if (
                property.IsTemporary
                && (
                    changeType is AuditChangeType.Created
                    || (changeType is AuditChangeType.Updated && property.IsModified)
                )
            )
            {
                (temporaryValuePatches ??= []).Add(new TemporaryValuePatch(newValues, propertyName, property));
            }
        }

        // Skip updates with no real changed fields
        if (changeType == AuditChangeType.Updated && changedFields.Count == 0)
        {
            return null;
        }

        var action = _DetermineAction(changeType.Value, actionContext);
        var (entityType, entityId) = _GetEntityIdentity(entry);

        var data = new AuditLogEntryData
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

        // Created entries need EntityId re-resolution once store-generated keys are assigned;
        // any entry may additionally carry temporary property values that need patching post-save.
        if (changeType is AuditChangeType.Created || temporaryValuePatches is not null)
        {
            _deferredResolutions.Add(
                data,
                new DeferredEntryResolution(entry, changeType is AuditChangeType.Created, temporaryValuePatches)
            );
        }

        return data;
    }

    private static void _CaptureActionFlags(ActionContext context, string propertyName, PropertyEntry property)
    {
        if (!property.IsModified)
        {
            return;
        }

        if (string.Equals(propertyName, nameof(IDeleteAudit.IsDeleted), StringComparison.Ordinal))
        {
            var nowDeleted = property.CurrentValue is true;
            var wasDeleted = property.OriginalValue is true;

            context.IsSoftDeleted = !wasDeleted && nowDeleted;
            context.IsRestored = wasDeleted && !nowDeleted;
            return;
        }

        if (string.Equals(propertyName, nameof(ISuspendAudit.IsSuspended), StringComparison.Ordinal))
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
            {
                return AuditActionNames.SoftDeleted;
            }

            if (context.IsRestored)
            {
                return AuditActionNames.Restored;
            }

            if (context.IsSuspended)
            {
                return AuditActionNames.Suspended;
            }

            if (context.IsUnsuspended)
            {
                return AuditActionNames.Unsuspended;
            }
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
                        ["SensitiveValueTransformer must be configured when SensitiveDataStrategy is Transform."]
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
                    _logger.LogSensitiveTransformerFailed(ex, clrType.FullName ?? clrType.Name, propertyName);

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
        {
            return (entry.Metadata.ClrType.FullName, null);
        }

        var values = key.Properties.Select(p => entry.Property(p.Name).CurrentValue).ToArray();

        return (entry.Metadata.ClrType.FullName, _FormatEntityId(values));
    }

    private static string? _FormatEntityId(object?[] values)
    {
        if (values.Length == 0)
        {
            return null;
        }

        if (values.Length == 1)
        {
            return values[0]?.ToString();
        }

        return JsonSerializer.Serialize(values.Select(static value => value?.ToString()).ToArray());
    }

    private static AuditPropertyMetadata _GetPropertyMetadata(PropertyInfo propInfo)
    {
        var ownerType = propInfo.DeclaringType ?? propInfo.ReflectedType ?? typeof(object);
        var inner = _PropertyCache.GetValue(ownerType, _CreatePropertyInner);

        return inner.GetOrAdd(
            propInfo.Name,
            static (_, pi) =>
            {
                var ignore = pi.GetCustomAttribute<AuditIgnoreAttribute>();
                var sensitive = pi.GetCustomAttribute<AuditSensitiveAttribute>();

                return new AuditPropertyMetadata(
                    IsIgnored: ignore is not null,
                    IsSensitive: sensitive is not null,
                    SensitiveStrategy: sensitive?.Strategy
                );
            },
            propInfo
        );
    }

    private sealed record AuditPropertyMetadata(
        bool IsIgnored,
        bool IsSensitive,
        SensitiveDataStrategy? SensitiveStrategy
    );

    private sealed record DeferredEntryResolution(
        EntityEntry Entry,
        bool ResolveEntityId,
        List<TemporaryValuePatch>? TemporaryValuePatches
    );

    private readonly record struct TemporaryValuePatch(
        Dictionary<string, object?> Target,
        string PropertyName,
        PropertyEntry Property
    );

    private sealed class ActionContext
    {
        public bool IsSoftDeleted { get; set; }

        public bool IsRestored { get; set; }

        public bool IsSuspended { get; set; }

        public bool IsUnsuspended { get; set; }
    }
}

internal static partial class EfAuditChangeCaptureLog
{
    // Error, not Warning: with CaptureErrorStrategy.Continue this entity's change is about to be
    // persisted without its audit row — operators must see that in logs.
    [LoggerMessage(
        EventId = 1,
        EventName = "AuditCaptureFailed",
        Level = LogLevel.Error,
        Message = "Audit capture failed for entity {EntityType}. Audit entry skipped; entity save continues."
    )]
    public static partial void LogAuditCaptureFailed(this ILogger logger, Exception exception, string? entityType);

    [LoggerMessage(
        EventId = 2,
        EventName = "AuditDisabled",
        Level = LogLevel.Warning,
        Message = "Audit logging is disabled. Set AuditLogOptions.IsEnabled = true to enable audit capture."
    )]
    public static partial void LogAuditDisabled(this ILogger logger);

    [LoggerMessage(
        EventId = 3,
        EventName = "SensitiveTransformerFailed",
        Level = LogLevel.Warning,
        Message = "Sensitive value transformer threw for {EntityType}.{PropertyName}. Falling back to Redact."
    )]
    public static partial void LogSensitiveTransformerFailed(
        this ILogger logger,
        Exception exception,
        string entityType,
        string propertyName
    );
}
