// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.CommitCoordination;

/// <summary>
/// Exposes the live relational connection and transaction to work that must write durable rows inside the
/// physical transaction before commit.
/// </summary>
/// <remarks>
/// This capability is attached by provider enlistment helpers (for example
/// <c>DatabaseFacade.EnlistCommitCoordination</c> for EF Core, or <c>NpgsqlConnection.EnlistCommitCoordination</c>
/// for PostgreSQL). Work buffers and callbacks retrieve it via
/// <see cref="ICommitCoordinator.TryGetCapability{TCapability}" /> or
/// <see cref="CommitContext.TryGetCapability{TCapability}" /> to write outbox rows or similar durable data
/// atomically within the transaction.
/// <para>
/// The properties return <see langword="null" /> after the transaction has closed (e.g. when accessed from a
/// post-commit callback after the transaction has been disposed). Callers should check for
/// <see langword="null" /> or only access these handles from <see cref="ICommitCoordinator.OnCommit" />
/// callbacks (where the transaction is still live at the point the buffer writes).
/// </para>
/// </remarks>
[PublicAPI]
public interface IRelationalCommitContext : ICommitCapability
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
