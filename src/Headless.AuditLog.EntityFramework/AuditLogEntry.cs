// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// Persistent audit log entity. Single table with JSON columns for old/new values.
/// </summary>
[AuditIgnore] // Prevent recursive capture when AuditByDefault is enabled
public sealed class AuditLogEntry
{
    /// <summary>Auto-generated sequential ID.</summary>
    public long Id { get; init; }

    /// <summary>UTC timestamp when the entry was captured.</summary>
    public DateTime CreatedAt { get; init; }

    // Actor
    /// <summary>ID of the user who triggered the change.</summary>
    public string? UserId { get; init; }

    /// <summary>Account ID of the user (if applicable).</summary>
    public string? AccountId { get; init; }

    /// <summary>Tenant the change belongs to.</summary>
    public string? TenantId { get; init; }

    /// <summary>Client IP address (if available).</summary>
    /// <remarks>
    /// Not populated by automatic change capture. Consumers must set this
    /// via explicit <see cref="IAuditLog.LogAsync"/> calls or a custom
    /// <see cref="IAuditChangeCapture"/> implementation.
    /// </remarks>
    public string? IpAddress { get; init; }

    /// <summary>HTTP User-Agent string (if available).</summary>
    /// <remarks>
    /// Not populated by automatic change capture. Consumers must set this
    /// via explicit <see cref="IAuditLog.LogAsync"/> calls or a custom
    /// <see cref="IAuditChangeCapture"/> implementation.
    /// </remarks>
    public string? UserAgent { get; init; }

    /// <summary>Correlation ID grouping related operations.</summary>
    public string? CorrelationId { get; init; }

    // Action
    /// <summary>Human-readable action name (e.g., <c>"entity.created"</c>).</summary>
    public required string Action { get; init; }

    /// <summary>EF change type, or <c>null</c> for explicit events.</summary>
    public AuditChangeType? ChangeType { get; init; }

    // Entity
    /// <summary>Full CLR type name of the affected entity.</summary>
    public string? EntityType { get; init; }

    /// <summary>
    /// String representation of the entity's primary key.
    /// Composite keys are encoded as a JSON array of string values.
    /// </summary>
    public string? EntityId { get; init; }

    // Changes — stored as string columns, serialized via value converters
    /// <summary>Property values before the change. <c>null</c> for Created entries.</summary>
    /// <remarks>
    /// Values are serialized as their CLR types on write but deserialize as
    /// <see cref="System.Text.Json.JsonElement"/> on read. Use <c>GetDecimal()</c>,
    /// <c>GetInt32()</c>, etc. to extract typed values.
    /// </remarks>
    public Dictionary<string, object?>? OldValues { get; init; }

    /// <summary>Property values after the change. <c>null</c> for Deleted entries.</summary>
    /// <remarks>
    /// Values are serialized as their CLR types on write but deserialize as
    /// <see cref="System.Text.Json.JsonElement"/> on read. Use <c>GetDecimal()</c>,
    /// <c>GetInt32()</c>, etc. to extract typed values.
    /// </remarks>
    public Dictionary<string, object?>? NewValues { get; init; }

    /// <summary>Names of properties that changed. Non-null for Updated entries.</summary>
    public List<string>? ChangedFields { get; init; }

    // Outcome
    /// <summary>Whether the operation succeeded.</summary>
    public bool Success { get; init; } = true;

    /// <summary>Error code if the operation failed.</summary>
    public string? ErrorCode { get; init; }
}
