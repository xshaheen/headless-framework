// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.Hosting.Initialization.Schema;

/// <summary>
/// The database-specific half of the Headless schema runner: the per-database session lock, the history table's SQL,
/// parameter creation, and driver error classification. The runner in <c>Headless.Hosting</c> stays driver-free;
/// implementations live in <c>Headless.Sql.PostgreSql</c> and <c>Headless.Sql.SqlServer</c>.
/// </summary>
[PublicAPI]
public interface ISchemaDialect
{
    /// <summary>The dialect's stable name. Contributions group by it together with the database identity.</summary>
    string Name { get; }

    /// <summary>
    /// The line the exported script places between batches, such as <c>GO</c> for <c>sqlcmd</c>, or
    /// <see langword="null"/> when the dialect's client runs one file as a single script.
    /// </summary>
    string? ScriptBatchSeparator { get; }

    /// <summary>
    /// Returns a stable identity for the database an unopened <paramref name="connection"/> targets. Two connection
    /// strings that reach the same database must yield the same identity, because the runner keys its lock and its
    /// single pass per database on it.
    /// </summary>
    string DatabaseIdentity(DbConnection connection);

    /// <summary>Creates a string command parameter.</summary>
    DbParameter CreateStringParameter(string name, string value);

    /// <summary>
    /// Returns a statement that tries once, without waiting, to take the runner's session lock named by the
    /// <c>@LockResource</c> parameter, and returns a single scalar that is true or non-zero when it succeeded. The
    /// runner polls it; a blocking acquisition would hold a snapshot open while it waits, which deadlocks against
    /// PostgreSQL's <c>CREATE INDEX CONCURRENTLY</c>.
    /// </summary>
    string TryAcquireLockSql { get; }

    /// <summary>Returns the statement that releases the session lock named by <c>@LockResource</c>. Must not throw when the lock is not held.</summary>
    string ReleaseLockSql { get; }

    /// <summary>Returns the idempotent DDL that creates <paramref name="schema"/> and its history table.</summary>
    string HistoryTableSql(string schema);

    /// <summary>
    /// Returns the unquoted name, within <paramref name="schema"/>, under which the history table of
    /// <paramref name="schema"/> is stored: <see cref="SchemaRunner.HistoryTableName"/>, or that name with the schema
    /// as a prefix on a database without schemas. A tool that clears data between tests keeps the table by this name,
    /// because the runner trusts the history and never recreates a table whose step it records.
    /// </summary>
    string HistoryTableName(string schema);

    /// <summary>
    /// Returns the query that reads the history table of <paramref name="schema"/>, projecting feature, step version,
    /// checksum, and description, in that order.
    /// </summary>
    string ReadHistorySql(string schema);

    /// <summary>
    /// Returns the idempotent statement that records one step in the history table of <paramref name="schema"/> from
    /// the <c>@Feature</c>, <c>@StepVersion</c>, <c>@Description</c>, and <c>@Checksum</c> parameters. It does nothing
    /// when the row exists, so a re-run deploy script stays safe.
    /// </summary>
    string InsertHistorySql(string schema);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="exception"/> means another creator committed the same object
    /// first. The runner then re-runs the step once in a fresh transaction.
    /// </summary>
    bool IsAlreadyCreatedRace(Exception exception);

    /// <summary>Returns <see langword="true"/> when <paramref name="exception"/> means the history table or its schema does not exist.</summary>
    bool IsObjectNotFound(Exception exception);
}
