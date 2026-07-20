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
    /// Unspecified filters are not applied.
    /// </summary>
    /// <param name="query">The filters and result limit to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="AuditLogQuery.Limit"/> is less than one.</exception>
    Task<IReadOnlyList<AuditLogEntryData>> QueryAsync(
        AuditLogQuery query,
        CancellationToken cancellationToken = default
    );
}
