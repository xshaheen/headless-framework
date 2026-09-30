// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// What the schema runner conformance suite needs from one database provider. A fixture registers the real pilot
/// features (Sequences, Idempotency, Coordination) against its container, one service provider per simulated replica.
/// </summary>
public interface ISchemaRunnerFixture
{
    /// <summary>The dialect under test.</summary>
    ISchemaDialect Dialect { get; }

    /// <summary>History rows a complete apply records: every pilot feature's step count, summed.</summary>
    int ExpectedStepCount { get; }

    /// <summary>Tables a complete apply leaves in the schema, the history table included.</summary>
    int ExpectedTableCount { get; }

    /// <summary>
    /// Whether the foreign-creator scenario deterministically fails the runner's first attempt and forces a re-run.
    /// PostgreSQL does, on the catalog's unique index; SQL Server's catalog visibility lets the guard see the committed
    /// object instead, so the race resolves without an error there.
    /// </summary>
    bool ForeignCreatorForcesRerun { get; }

    /// <summary>
    /// Builds a runner from a fresh service provider that registers the three pilot features with their schema set to
    /// <paramref name="schema"/>, as one replica of a deployment would.
    /// </summary>
    SchemaRunner CreateRunner(string schema);

    /// <summary>Builds a host whose only hosted service is the runner of <see cref="CreateRunner"/>, in <paramref name="mode"/>.</summary>
    IHost CreateHost(string schema, SchemaRunnerMode mode);

    /// <summary>Drops <paramref name="schema"/> and everything in it, if it exists.</summary>
    Task DropSchemaAsync(string schema, CancellationToken cancellationToken);

    /// <summary>Runs <paramref name="script"/> with the provider's plain client, with no runner, lock, or history logic involved.</summary>
    Task ExecuteScriptWithPlainClientAsync(string script, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a transaction that creates <paramref name="schema"/> and the Sequences table the way a consumer's EF
    /// migration would, and keeps it open until <see cref="IForeignCreator.CommitAsync"/>.
    /// </summary>
    Task<IForeignCreator> BeginForeignCreatorAsync(string schema, CancellationToken cancellationToken);

    /// <summary>Overwrites the recorded checksum of one history row, as if the step's SQL had been edited after it shipped.</summary>
    Task TamperChecksumAsync(string schema, string feature, string version, CancellationToken cancellationToken);

    /// <summary>Deletes one history row, as if the code gained a step the database has not received.</summary>
    Task DeleteHistoryRowAsync(string schema, string feature, string version, CancellationToken cancellationToken);

    /// <summary>Counts the rows of the schema's history table.</summary>
    Task<int> CountHistoryRowsAsync(string schema, CancellationToken cancellationToken);

    /// <summary>Counts the base tables in <paramref name="schema"/>.</summary>
    Task<int> CountTablesAsync(string schema, CancellationToken cancellationToken);

    /// <summary>
    /// Runs the protocol of the deleted hand-written initializers for every pilot contribution in
    /// <paramref name="schema"/>: per feature, its own connection, its own feature lock plus the schema lock, and its
    /// whole DDL batch in one transaction. Used only to measure the old startup cost against the runner's.
    /// </summary>
    Task RunLegacyInitializerProtocolAsync(string schema, CancellationToken cancellationToken);
}

/// <summary>A transaction outside the runner that has created objects the runner is about to create.</summary>
public interface IForeignCreator : IAsyncDisposable
{
    /// <summary>Returns whether another session is currently waiting on a lock this transaction holds.</summary>
    Task<bool> IsBlockingAnotherSessionAsync(CancellationToken cancellationToken);

    /// <summary>Commits the foreign transaction.</summary>
    Task CommitAsync(CancellationToken cancellationToken);
}
