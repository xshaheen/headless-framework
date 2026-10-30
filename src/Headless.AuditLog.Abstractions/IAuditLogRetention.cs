// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// Deletes audit entries past their retention period. It is the sanctioned way to remove audit rows, and it keeps
/// working when the table is append-only (<c>AuditLogStorageOptions.AppendOnly</c>), whose trigger refuses every
/// other <c>DELETE</c>.
/// </summary>
/// <typeparam name="TContext">
/// The persistence context type that owns the audit log table, matching <see cref="IReadAuditLog{TContext}"/>.
/// </typeparam>
/// <remarks>
/// Call it from a scheduled job. Each call deletes at most <c>limit</c> of the oldest matching entries in one
/// transaction, so call it again while it returns <c>limit</c>.
/// </remarks>
public interface IAuditLogRetention<TContext>
{
    /// <summary>Deletes up to <paramref name="limit"/> entries created more than <paramref name="olderThan"/> ago, oldest first.</summary>
    /// <param name="olderThan">The retention period, measured back from the registered <see cref="TimeProvider"/>'s current time.</param>
    /// <param name="limit">The most entries to delete in this call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of entries deleted.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="olderThan"/> is not positive, or <paramref name="limit"/> is less than one.
    /// </exception>
    Task<int> PurgeAsync(TimeSpan olderThan, int limit, CancellationToken cancellationToken = default);
}
