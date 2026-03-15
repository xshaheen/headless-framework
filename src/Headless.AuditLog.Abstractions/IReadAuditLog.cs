// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// Read abstraction for querying persisted audit log entries.
/// Decouples consumers from the storage implementation.
/// </summary>
public interface IReadAuditLog
{
    /// <summary>
    /// Queries audit log entries matching the specified filters.
    /// All parameters are optional; omitted parameters are not filtered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pagination (v1):</b> This method supports limit-only pagination.
    /// It returns up to <paramref name="limit"/> entries (default 100) ordered by
    /// most recent first. Cursor-based and offset pagination are planned for a
    /// future release.
    /// </para>
    /// </remarks>
    /// <param name="action">Filter by action name (exact match).</param>
    /// <param name="entityType">Filter by entity type (exact match).</param>
    /// <param name="entityId">Filter by entity ID (exact match).</param>
    /// <param name="userId">Filter by user ID.</param>
    /// <param name="tenantId">Filter by tenant ID.</param>
    /// <param name="from">Include entries created at or after this time.</param>
    /// <param name="to">Include entries created before this time.</param>
    /// <param name="limit">
    /// Maximum number of entries to return. Defaults to 100 when not specified.
    /// This is the only pagination mechanism in v1; cursor-based pagination is
    /// planned for a future release.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AuditLogEntryData>> QueryAsync(
        string? action = null,
        string? entityType = null,
        string? entityId = null,
        string? userId = null,
        string? tenantId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int limit = 100,
        CancellationToken cancellationToken = default
    );
}
