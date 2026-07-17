// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>Specifies filters for querying persisted audit log entries.</summary>
[PublicAPI]
public sealed class AuditLogQuery
{
    /// <summary>Creates a query with no filters and a result limit of 100.</summary>
    public AuditLogQuery() { }

    /// <summary>Gets the optional exact action-name filter.</summary>
    public string? Action { get; init; }

    /// <summary>Gets the optional exact entity-type filter.</summary>
    public string? EntityType { get; init; }

    /// <summary>Gets the optional exact entity-ID filter.</summary>
    public string? EntityId { get; init; }

    /// <summary>Gets the optional user-ID filter.</summary>
    public string? UserId { get; init; }

    /// <summary>Gets the optional tenant-ID filter.</summary>
    public string? TenantId { get; init; }

    /// <summary>Gets the inclusive lower bound for the entry creation time.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Gets the exclusive upper bound for the entry creation time.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Gets the maximum number of entries to return. The default is 100.</summary>
    /// <remarks>Storage implementations reject values less than one before executing a query.</remarks>
    public int Limit { get; init; } = 100;
}
