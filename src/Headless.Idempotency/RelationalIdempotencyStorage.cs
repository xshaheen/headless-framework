// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Sql;
using Headless.UnitOfWork;

namespace Headless.Idempotency;

/// <summary>What a relational provider package supplies: its dialect, its database, and how it begins an owned unit.</summary>
/// <param name="Dialect">The engine's dialect.</param>
/// <param name="PackageName">The provider package, for messages.</param>
/// <param name="ConnectionString">The connection string of the database that holds the records.</param>
/// <param name="CommandTimeoutSeconds">The timeout of every command.</param>
/// <param name="Table">The record table, named by the dialect in the configured schema.</param>
/// <param name="BeginOwnedUnit">
/// Begins an owned unit of work on a new connection at READ COMMITTED, through the provider's typed unit-of-work entry.
/// </param>
/// <param name="BeginReadOnlyTransaction">
/// Begins the transaction of an autonomous read that must not wait on a writer, or <see langword="null" /> to read in
/// the ordinary autonomous transaction.
/// </param>
/// <param name="EnlistedAdmissionRefusal">
/// Why an admission inside a caller's unit is refused, or <see langword="null" /> when it is accepted.
/// </param>
internal sealed record RelationalIdempotencyStorage(
    ISqlDialect Dialect,
    string PackageName,
    string ConnectionString,
    int CommandTimeoutSeconds,
    IdempotencyTable Table,
    Func<IUnitOfWorkFactory, DbConnection, CancellationToken, ValueTask<IUnitOfWork>> BeginOwnedUnit,
    Func<DbConnection, CancellationToken, ValueTask<DbTransaction>>? BeginReadOnlyTransaction = null,
    string? EnlistedAdmissionRefusal = null
)
{
    public DbConnection CreateConnection() => Dialect.CreateConnection(ConnectionString);
}
