// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>Determines how sensitive property values are handled during audit capture.</summary>
public enum SensitiveDataStrategy
{
    /// <summary>
    /// Replace the value with <c>"***"</c>. The property name still appears in
    /// <see cref="AuditLogEntryData.ChangedFields"/> — you know it changed, not to what.
    /// </summary>
    Redact,

    /// <summary>
    /// Omit the property entirely from OldValues, NewValues, and ChangedFields.
    /// </summary>
    Exclude,

    /// <summary>
    /// Pass the value through <see cref="AuditLogOptions.SensitiveValueTransformer"/>.
    /// Consumer controls the output (hash, mask, tokenize).
    /// </summary>
    Transform,
}
