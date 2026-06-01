// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.AuditLog;

/// <summary>
/// Resolves the ambient <see cref="DbConnection"/> and <see cref="DbTransaction"/> for a
/// given <c>savingContext</c>, allowing raw-SQL audit stores to enroll their writes in the
/// consumer's active transaction instead of opening a separate connection.
/// </summary>
/// <remarks>
/// Implementations live in packages that own the persistence pipeline (e.g. the EF Core
/// integration). The interface deals only in BCL <see cref="System.Data.Common"/> base types
/// so raw provider packages can depend on it without referencing EF Core.
/// </remarks>
public interface IAmbientDbTransactionAccessor
{
    /// <summary>
    /// Returns the active connection and transaction associated with <paramref name="savingContext"/>,
    /// or <c>(null, null)</c> when no ambient transaction is in flight.
    /// </summary>
    /// <remarks>
    /// Implementations MUST NOT open new connections or begin new transactions. The returned
    /// connection and transaction are owned by the caller of <c>SaveChanges</c>; the audit
    /// store reuses them in read/append mode only.
    /// </remarks>
    (DbConnection? Connection, DbTransaction? Transaction) TryResolve(object savingContext);
}
