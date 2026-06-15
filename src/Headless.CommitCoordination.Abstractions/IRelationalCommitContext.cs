// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.CommitCoordination;

/// <summary>
/// Provides relational handles for work that must write durable rows inside the physical transaction.
/// </summary>
[PublicAPI]
public interface IRelationalCommitContext : ICommitCapability
{
    /// <summary>
    /// Gets the active database connection when it is still available.
    /// </summary>
    DbConnection? Connection { get; }

    /// <summary>
    /// Gets the active database transaction when it is still available.
    /// </summary>
    DbTransaction? Transaction { get; }
}
