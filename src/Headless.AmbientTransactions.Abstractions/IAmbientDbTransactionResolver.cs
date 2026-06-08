// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.AmbientTransactions;

/// <summary>
/// Resolves the active provider connection and transaction for a persistence context.
/// </summary>
/// <remarks>
/// Implementations must not open connections or begin transactions. The returned objects are owned by the caller.
/// </remarks>
[PublicAPI]
public interface IAmbientDbTransactionResolver
{
    /// <summary>
    /// Returns the active connection and transaction associated with <paramref name="savingContext" />,
    /// or <c>(null, null)</c> when no ambient transaction is in flight.
    /// </summary>
    (DbConnection? Connection, DbTransaction? Transaction) TryResolve(object savingContext);
}
