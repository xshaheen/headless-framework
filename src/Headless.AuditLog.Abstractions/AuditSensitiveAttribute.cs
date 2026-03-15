// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// Marks a property as containing sensitive data (PII, secrets, credentials).
/// The value will be handled according to the configured <see cref="SensitiveDataStrategy"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditSensitiveAttribute : Attribute
{
    /// <summary>
    /// Override the global <see cref="AuditLogOptions.SensitiveDataStrategy"/> for this property.
    /// When <c>null</c>, falls back to the global strategy.
    /// </summary>
    public SensitiveDataStrategy? Strategy { get; init; }
}
