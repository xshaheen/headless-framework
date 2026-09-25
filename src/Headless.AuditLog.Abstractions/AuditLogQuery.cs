// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.AuditLog;

/// <summary>Specifies filters and keyset paging for querying persisted audit log entries.</summary>
/// <remarks>
/// <see cref="ContinuationPageRequest.ContinuationToken"/> takes a previous page's
/// <see cref="ContinuationPage{T}.ContinuationToken"/>; the next page starts strictly after that page's last entry in
/// <see cref="Direction"/> order. Send it with the same filters and direction that produced it. Entries written after
/// the first page was read appear on a later page only when they sort after the token. <see cref="ContinuationPageRequest.Size"/>
/// defaults to 100; storage implementations reject values less than one.
/// </remarks>
[PublicAPI]
public sealed class AuditLogQuery : ContinuationPageRequest
{
    /// <summary>Creates a query with no filters that returns the newest 100 entries.</summary>
    public AuditLogQuery()
    {
        Size = 100;
    }

    /// <summary>Gets the optional exact action-name filter.</summary>
    public string? Action { get; init; }

    /// <summary>Gets the optional exact entity-type filter.</summary>
    public string? EntityType { get; init; }

    /// <summary>Gets the optional exact entity-ID filter.</summary>
    public string? EntityId { get; init; }

    /// <summary>Gets the optional user-ID filter, the actor that triggered the entry.</summary>
    public string? UserId { get; init; }

    /// <summary>Gets the optional account-ID filter.</summary>
    public string? AccountId { get; init; }

    /// <summary>Gets the optional tenant-ID filter.</summary>
    public string? TenantId { get; init; }

    /// <summary>Gets the optional correlation-ID filter, grouping the entries of one logical operation.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Gets the inclusive lower bound for the entry creation time.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Gets the exclusive upper bound for the entry creation time.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Gets the order entries are returned in. The default is <see cref="AuditLogSortDirection.NewestFirst"/>.</summary>
    public AuditLogSortDirection Direction { get; init; } = AuditLogSortDirection.NewestFirst;
}

/// <summary>The order in which <see cref="IReadAuditLog{TContext}"/> returns entries.</summary>
public enum AuditLogSortDirection
{
    /// <summary>Most recent entries first: descending creation time, then descending ID.</summary>
    NewestFirst = 0,

    /// <summary>Oldest entries first: ascending creation time, then ascending ID.</summary>
    OldestFirst = 1,
}
