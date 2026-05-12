// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

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
    /// When <c>true</c>, entities are audited by default unless decorated with
    /// <see cref="AuditIgnoreAttribute"/> at the class level.
    /// When <c>false</c> (default), only entities implementing <see cref="IAuditTracked"/> are audited.
    /// </summary>
    public bool AuditByDefault { get; set; }

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
    /// The first result for a given entity type is cached for the capture service lifetime.
    /// Return <c>true</c> to exclude. The predicate must be pure and deterministic.
    /// </summary>
    public Func<Type, bool>? EntityFilter { get; set; }

    /// <summary>
    /// Predicate to exclude specific properties from change tracking.
    /// The first result for a given entity type and property name is cached for the capture
    /// service lifetime. Return <c>true</c> to exclude. The predicate must be pure and deterministic.
    /// Applied after attribute-based filtering and default excluded property checks.
    /// </summary>
    public Func<Type, string, bool>? PropertyFilter { get; set; }

    /// <summary>
    /// Framework-managed property names excluded by default during change capture.
    /// Consumers can add, remove, or clear entries to match their model.
    /// </summary>
    public HashSet<string> DefaultExcludedProperties { get; set; } =
        new(StringComparer.Ordinal)
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

    /// <summary>
    /// Strategy applied when audit capture (<c>IAuditChangeCapture.CaptureChanges</c>) throws.
    /// Default: <see cref="CaptureErrorStrategy.Continue"/> — log an error and continue the entity save without audit entries for that batch.
    /// Set to <see cref="CaptureErrorStrategy.Throw"/> to abort the save when audit capture fails.
    /// </summary>
    public CaptureErrorStrategy CaptureErrorStrategy { get; set; } = CaptureErrorStrategy.Continue;
}

/// <summary>Strategy applied when audit capture throws.</summary>
public enum CaptureErrorStrategy
{
    /// <summary>Log the failure as an error and continue the entity save without audit entries for that batch.</summary>
    Continue = 0,

    /// <summary>Log the failure and rethrow so the entity save is aborted.</summary>
    Throw = 1,
}

/// <summary>Validates <see cref="AuditLogOptions"/>.</summary>
public sealed class AuditLogOptionsValidator : AbstractValidator<AuditLogOptions>
{
    public AuditLogOptionsValidator()
    {
        RuleFor(x => x.DefaultExcludedProperties).NotNull();

        RuleFor(x => x.SensitiveValueTransformer)
            .NotNull()
            .When(x => x.SensitiveDataStrategy == SensitiveDataStrategy.Transform)
            .WithMessage("SensitiveValueTransformer must be configured when SensitiveDataStrategy is Transform.");
    }
}
