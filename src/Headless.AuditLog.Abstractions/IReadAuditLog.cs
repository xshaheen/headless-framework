// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// Read abstraction for querying persisted audit log entries.
/// Decouples consumers from the storage implementation.
/// </summary>
/// <typeparam name="TContext">
/// The persistence context type that owns the audit log table. Typed at this level so that
/// multi-context applications resolve a distinct <see cref="IReadAuditLog{TContext}"/> per context
/// instead of binding to whichever context happened to register first.
/// </typeparam>
/// <remarks>
/// <typeparamref name="TContext"/> is the EF Core <c>DbContext</c> type that owns the audit log table.
/// No EF constraint is applied here so this abstractions package can stay free of the EF Core dependency.
/// </remarks>
public interface IReadAuditLog<TContext>
{
    /// <summary>
    /// Queries audit log entries matching the specified filters.
    /// All parameters are optional; omitted parameters are not filtered.
    /// </summary>
    /// <param name="action">Filter by action name (exact match).</param>
    /// <param name="entityType">Filter by entity type (exact match).</param>
    /// <param name="entityId">Filter by entity ID (exact match).</param>
    /// <param name="userId">Filter by user ID.</param>
    /// <param name="tenantId">Filter by tenant ID.</param>
    /// <param name="from">Include entries created at or after this time.</param>
    /// <param name="to">Include entries created before this time.</param>
    /// <param name="limit">Maximum entries to return. Default: 100.</param>
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
