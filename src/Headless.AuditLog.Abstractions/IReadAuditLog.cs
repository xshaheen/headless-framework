// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

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
    /// Queries one page of audit log entries matching the specified filters, ordered by creation time and
    /// then by entry ID in <see cref="AuditLogQuery.Direction"/>. Unspecified filters are not applied.
    /// </summary>
    /// <param name="query">The filters, direction, page size, and continuation token to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The page of entries. Its <see cref="ContinuationPage{T}.ContinuationToken"/> fetches the next page and is
    /// <see langword="null"/> when no further entries match.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="ContinuationPageRequest.Size"/> is less than one or equal to int.MaxValue.</exception>
    /// <exception cref="ArgumentException"><see cref="ContinuationPageRequest.ContinuationToken"/> is not a token this API issued.</exception>
    Task<ContinuationPage<AuditLogEntryData>> QueryAsync(
        AuditLogQuery query,
        CancellationToken cancellationToken = default
    );
}
