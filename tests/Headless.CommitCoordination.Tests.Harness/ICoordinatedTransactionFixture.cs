// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;

namespace Tests;

/// <summary>
/// Provider fixture for the <c>ExecuteCoordinatedTransactionAsync</c> helper conformance scenarios.
/// Each leaf wraps its provider's helper (EF <c>DbContext</c>/SQLite, raw <c>NpgsqlConnection</c>,
/// raw <c>SqlConnection</c>) and a durable probe table created at fixture initialization — never inside
/// the coordinated transaction (transactional DDL would vanish on rollback, and SQL Server temp tables
/// are invisible to the verifying connection).
/// </summary>
public interface ICoordinatedTransactionFixture
{
    /// <summary>
    /// Runs <paramref name="operation" /> through the provider's <c>ExecuteCoordinatedTransactionAsync</c>
    /// helper. The helper owns open/begin/enlist/commit; operation exceptions propagate to the caller.
    /// </summary>
    Task RunCoordinatedAsync(
        Func<ICoordinatedTransactionContext, CancellationToken, Task> operation,
        CancellationToken cancellationToken
    );

    /// <summary>Counts probe rows using a connection/context independent of the coordinated transaction.</summary>
    Task<int> CountProbeRowsAsync(CancellationToken cancellationToken);

    /// <summary>Deletes all probe rows between scenarios.</summary>
    Task ResetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Provider-agnostic surface handed to a conformance operation while it runs inside the helper's
/// coordinated transaction.
/// </summary>
public interface ICoordinatedTransactionContext
{
    /// <summary>
    /// The ambient coordinator the helper enlisted. Implementations must read
    /// <c>ICurrentCommitCoordinator.Current</c> lazily on each access (an execution-strategy retry opens a
    /// fresh coordinator) and throw when absent — an absent ambient coordinator means the helper failed to
    /// enlist, which is itself a conformance failure.
    /// </summary>
    ICommitCoordinator Coordinator { get; }

    /// <summary>Inserts one probe row inside the coordinated transaction.</summary>
    Task InsertProbeRowAsync(string name, CancellationToken cancellationToken);
}
