// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// Marks a property as containing sensitive data (PII, secrets, credentials).
/// The value will be handled according to the configured <see cref="SensitiveDataStrategy"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditSensitiveAttribute : Attribute
{
    /// <summary>Initializes the attribute with no explicit strategy (falls back to global).</summary>
    public AuditSensitiveAttribute() { }

    /// <summary>Initializes the attribute with an explicit per-property strategy.</summary>
    /// <param name="strategy">The strategy to apply for this property.</param>
    public AuditSensitiveAttribute(SensitiveDataStrategy strategy)
    {
        Strategy = strategy;
    }

    /// <summary>
    /// Override the global <see cref="AuditLogOptions.SensitiveDataStrategy"/> for this property.
    /// When <c>null</c>, falls back to the global strategy.
    /// </summary>
    public SensitiveDataStrategy? Strategy { get; }
}
