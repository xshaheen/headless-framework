// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Sql;
using Headless.UnitOfWork;

namespace Headless.Fencing;

/// <summary>What a relational provider package supplies: its dialect, its database, and how it begins an owned unit.</summary>
/// <param name="Dialect">The engine's dialect.</param>
/// <param name="PackageName">The provider package, for messages.</param>
/// <param name="ConnectionString">The connection string of the database that holds the leases.</param>
/// <param name="CommandTimeoutSeconds">The timeout of every command.</param>
/// <param name="InitializeOnStartup">Whether the storage initializer creates the objects at startup.</param>
/// <param name="Table">The lease table, named by the dialect in the configured schema.</param>
/// <param name="BeginOwnedUnit">
/// Begins an owned unit of work on a new connection at READ COMMITTED, through the provider's typed unit-of-work entry.
/// </param>
internal sealed record RelationalFencingStorage(
    ISqlDialect Dialect,
    string PackageName,
    string ConnectionString,
    int CommandTimeoutSeconds,
    bool InitializeOnStartup,
    FencingTable Table,
    Func<IUnitOfWorkFactory, DbConnection, CancellationToken, ValueTask<IUnitOfWork>> BeginOwnedUnit
)
{
    public DbConnection CreateConnection() => Dialect.CreateConnection(ConnectionString);
}
