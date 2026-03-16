// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// DTO representing one captured audit log entry.
/// Produced by the change-capture pipeline; consumed by <see cref="IAuditLogStore"/>.
/// </summary>
public sealed record AuditLogEntryData
{
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

    /// <summary>Correlation ID grouping related operations in one logical unit.</summary>
    public string? CorrelationId { get; init; }

    // Action
    /// <summary>Human-readable action name (e.g., <c>"entity.created"</c>, <c>"pii.revealed"</c>).</summary>
    public required string Action { get; init; }

    /// <summary>EF change type, or <c>null</c> for explicit (non-mutation) events.</summary>
    public AuditChangeType? ChangeType { get; init; }

    // Entity
    /// <summary>Full CLR type name of the affected entity.</summary>
    public string? EntityType { get; init; }

    /// <summary>
    /// String representation of the entity's primary key.
    /// Composite keys are encoded as a JSON array of string values.
    /// Mutable so the capture pipeline can patch in store-generated keys after save.
    /// </summary>
    public string? EntityId { get; set; }

    // Changes
    /// <summary>Property values before the change. <c>null</c> for Created entries.</summary>
    /// <remarks>
    /// Providers may serialize CLR values on write but deserialize them as
    /// <see cref="System.Text.Json.JsonElement"/> on read. Use <c>GetDecimal()</c>,
    /// <c>GetInt32()</c>, and similar APIs for typed access.
    /// </remarks>
    public Dictionary<string, object?>? OldValues { get; init; }

    /// <summary>Property values after the change. <c>null</c> for Deleted entries.</summary>
    /// <remarks>
    /// Providers may serialize CLR values on write but deserialize them as
    /// <see cref="System.Text.Json.JsonElement"/> on read. Use <c>GetDecimal()</c>,
    /// <c>GetInt32()</c>, and similar APIs for typed access.
    /// </remarks>
    public Dictionary<string, object?>? NewValues { get; init; }

    /// <summary>Names of properties that changed. Non-null for Updated entries.</summary>
    public List<string>? ChangedFields { get; init; }

    // Outcome
    /// <summary>Whether the operation succeeded. Default: <c>true</c>.</summary>
    public bool Success { get; init; } = true;

    /// <summary>Error code if the operation failed.</summary>
    public string? ErrorCode { get; init; }

    // Metadata
    /// <summary>UTC timestamp when the entry was captured.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
