// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Sql;

namespace Headless.Messaging.Persistence;

/// <summary>
/// What a relational provider package supplies to the one relational messaging storage: its dialect, how it opens a
/// connection to its database, the names it reports under, and the width its schema gives the lease holder column.
/// </summary>
internal sealed class RelationalMessagingStorage(
    ISqlDialect dialect,
    string providerName,
    Func<DbConnection> createConnection,
    int leaseHolderMaxLength
)
{
    public ISqlDialect Dialect { get; } = Argument.IsNotNull(dialect);

    public string ProviderName { get; } = Argument.IsNotNullOrWhiteSpace(providerName);

    public Func<DbConnection> CreateConnection { get; } = Argument.IsNotNull(createConnection);

    public int LeaseHolderMaxLength { get; } = Argument.IsPositive(leaseHolderMaxLength);
}
