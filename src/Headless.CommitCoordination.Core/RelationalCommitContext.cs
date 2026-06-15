// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.CommitCoordination;

/// <summary>
/// Default relational commit capability backed by provider-owned connection and transaction delegates. Internal:
/// provider packages construct it via <c>InternalsVisibleTo</c>; consumers query the capability through
/// <see cref="IRelationalCommitContext" />.
/// </summary>
internal sealed class RelationalCommitContext(Func<DbConnection?> connection, Func<DbTransaction?> transaction)
    : IRelationalCommitContext
{
    /// <inheritdoc />
    public DbConnection? Connection => connection();

    /// <inheritdoc />
    public DbTransaction? Transaction => transaction();
}
