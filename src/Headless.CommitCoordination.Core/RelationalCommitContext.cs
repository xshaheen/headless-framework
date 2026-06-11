// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.CommitCoordination;

/// <summary>
/// Default relational commit capability backed by provider-owned connection and transaction delegates.
/// </summary>
[PublicAPI]
public sealed class RelationalCommitContext(
    Func<DbConnection?> connection,
    Func<DbTransaction?> transaction
) : IRelationalCommitContext
{
    /// <inheritdoc />
    public DbConnection? Connection => connection();

    /// <inheritdoc />
    public DbTransaction? Transaction => transaction();
}
