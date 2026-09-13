// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.CommitCoordination;

/// <summary>
/// Exposes the live relational connection and transaction to work that must write durable rows inside the
/// physical transaction before commit.
/// </summary>
/// <remarks>
/// The handle is supplied by the provider enlistment helper that opens the scope (for example
/// <c>DatabaseFacade.EnlistCommitCoordination</c> for EF Core, or <c>NpgsqlConnection.EnlistCommitCoordination</c>
/// for PostgreSQL) and surfaces as <see cref="ICommitCoordinator.Relational" />, so outbox and job writers can
/// place their rows atomically within the caller's transaction.
/// <para>
/// The properties return <see langword="null" /> after the transaction has closed (e.g. when accessed from a
/// post-commit callback after the transaction has been disposed). Callers should check for
/// <see langword="null" /> or only access these handles while the transaction is still live.
/// </para>
/// </remarks>
[PublicAPI]
public interface IRelationalCommitContext
{
    /// <summary>
    /// Gets the active database connection, or <see langword="null" /> if the connection is no longer available.
    /// </summary>
    DbConnection? Connection { get; }

    /// <summary>
    /// Gets the active database transaction, or <see langword="null" /> if the transaction has been closed.
    /// </summary>
    DbTransaction? Transaction { get; }
}
