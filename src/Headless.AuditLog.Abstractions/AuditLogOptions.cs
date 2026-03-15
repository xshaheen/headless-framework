// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>Configuration options for the audit log subsystem.</summary>
public sealed class AuditLogOptions
{
    /// <summary>
    /// Master enable/disable switch. When <c>false</c>, no audit entries are captured.
    /// Default: <c>true</c>.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, all entities are audited unless decorated with
    /// <see cref="AuditIgnoreAttribute"/> at the class level.
    /// When <c>false</c> (default), only entities implementing <see cref="IAuditTracked"/> are audited.
    /// </summary>
    public bool AuditAllEntities { get; set; }

    /// <summary>
    /// Global default strategy for properties marked with <see cref="AuditSensitiveAttribute"/>.
    /// Default: <see cref="SensitiveDataStrategy.Redact"/>.
    /// </summary>
    public SensitiveDataStrategy SensitiveDataStrategy { get; set; } = SensitiveDataStrategy.Redact;

    /// <summary>
    /// Called when the effective strategy is <see cref="SensitiveDataStrategy.Transform"/>.
    /// Must be a pure, synchronous function (mask, hash, truncate — no I/O).
    /// Runs inside the SaveChanges hot path.
    /// </summary>
    public Func<SensitiveValueContext, object?>? SensitiveValueTransformer { get; set; }

    /// <summary>
    /// Predicate to exclude specific entity types from change tracking.
    /// Called per entity type; result is cached per type. Return <c>true</c> to exclude.
    /// </summary>
    public Func<Type, bool>? EntityFilter { get; set; }

    /// <summary>
    /// Predicate to exclude specific properties from change tracking.
    /// Called per property; result is cached. Return <c>true</c> to exclude.
    /// Applied after attribute-based filtering.
    /// </summary>
    public Func<Type, string, bool>? PropertyFilter { get; set; }
}
