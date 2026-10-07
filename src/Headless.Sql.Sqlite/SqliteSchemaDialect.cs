// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Globalization;
using Headless.Checks;
using Headless.Hosting.Initialization.Schema;
using Microsoft.Data.Sqlite;

namespace Headless.Sql.Sqlite;

/// <summary>
/// The SQLite dialect of the Headless schema runner: a leased lock row per database in place of a session lock, and
/// a schema expressed as a name prefix, as <see cref="SqliteDialect" /> qualifies names.
/// </summary>
/// <remarks>
/// <para>
/// SQLite has no session or advisory locks, so the runner's lock is a row in <c>headless_schema_lock</c> naming its
/// holder: a random token kept in a temporary table, which lives as long as the runner's connection and is visible to
/// no other connection. The row is a lease that expires after <see cref="LockLease" />, so a runner that crashed
/// while holding it blocks the next start for at most that long.
/// </para>
/// <para>
/// The lock only spares replicas from applying the same steps one after another; correctness does not rest on it.
/// Every runner transaction takes the database write lock when it begins and SQLite's DDL is transactional, so two
/// runners' steps never interleave, and each step and history insert is idempotent. A lease that ran out under a slow
/// step therefore costs a second, harmless pass, never a broken schema. For the same reason no concurrent creator is
/// ever seen half-way, and <see cref="IsAlreadyCreatedRace" /> never matches.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class SqliteSchemaDialect : ISchemaDialect
{
    /// <summary>The lock table, one per database file whatever the schema.</summary>
    public const string LockTableName = "headless_schema_lock";

    /// <summary>
    /// How long a held lock row stays valid. Shorter than the runner's default lock wait, so a replica waiting on a
    /// crashed holder takes over instead of failing its start.
    /// </summary>
    public static readonly TimeSpan LockLease = TimeSpan.FromMinutes(1);

    private const string _OwnerTable = "temp.headless_schema_lock_owner";
    private const string _Now = "strftime('%Y-%m-%d %H:%M:%f', 'now')";

    private static readonly string _LeaseModifier = string.Create(
        CultureInfo.InvariantCulture,
        $"'+{(int)LockLease.TotalSeconds} seconds'"
    );

    /// <summary>The shared instance. The dialect holds no state.</summary>
    public static SqliteSchemaDialect Instance { get; } = new();

    /// <inheritdoc />
    public string Name => "Sqlite";

    /// <inheritdoc />
    /// <remarks>The <c>sqlite3</c> shell runs one file as a single script, so no separator is emitted.</remarks>
    public string? ScriptBatchSeparator => null;

    /// <inheritdoc />
    /// <remarks>
    /// Takes the row when it is free, expired, or already this connection's, then reports whether this connection
    /// holds it. Each statement commits on its own, so the poll holds no transaction between attempts.
    /// </remarks>
    public string TryAcquireLockSql =>
        $"""
            CREATE TEMP TABLE IF NOT EXISTS headless_schema_lock_owner (token BLOB NOT NULL);
            INSERT INTO {_OwnerTable} (token)
            SELECT randomblob(16) WHERE NOT EXISTS (SELECT 1 FROM {_OwnerTable});
            CREATE TABLE IF NOT EXISTS main."{LockTableName}" (
                resource TEXT NOT NULL PRIMARY KEY,
                owner BLOB NOT NULL,
                expires_at TEXT NOT NULL
            );
            INSERT INTO main."{LockTableName}" (resource, owner, expires_at)
            VALUES (@LockResource, (SELECT token FROM {_OwnerTable}), strftime('%Y-%m-%d %H:%M:%f', 'now', {_LeaseModifier}))
            ON CONFLICT (resource) DO UPDATE SET owner = excluded.owner, expires_at = excluded.expires_at
            WHERE "{LockTableName}".expires_at < {_Now} OR "{LockTableName}".owner = excluded.owner;
            SELECT EXISTS (
                SELECT 1 FROM main."{LockTableName}"
                WHERE resource = @LockResource AND owner = (SELECT token FROM {_OwnerTable})
            );
            """;

    /// <inheritdoc />
    /// <remarks>Deletes the row only while this connection holds it, so an expired lease another runner took survives.</remarks>
    public string ReleaseLockSql =>
        $"""
            DELETE FROM main."{LockTableName}"
            WHERE resource = @LockResource AND owner = (SELECT token FROM {_OwnerTable});
            """;

    /// <inheritdoc />
    /// <remarks>
    /// The full path of the database file. An in-memory or URI data source is returned as written: an in-memory
    /// database is private to its connection unless shared by name, and then the name is its identity.
    /// </remarks>
    public string DatabaseIdentity(DbConnection connection)
    {
        Argument.IsNotNull(connection);

        var builder = new SqliteConnectionStringBuilder(connection.ConnectionString);
        var dataSource = builder.DataSource;

        if (
            builder.Mode == SqliteOpenMode.Memory
            || string.IsNullOrEmpty(dataSource)
            || string.Equals(dataSource, ":memory:", StringComparison.Ordinal)
            || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
        )
        {
            return dataSource;
        }

        return Path.GetFullPath(dataSource);
    }

    /// <inheritdoc />
    public DbParameter CreateStringParameter(string name, string value)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsNotNull(value);

        return new SqliteParameter(name, SqliteType.Text) { Value = value };
    }

    /// <inheritdoc />
    /// <remarks>There is no schema to create: the history table's name carries the schema as a prefix.</remarks>
    public string HistoryTableSql(string schema)
    {
        return $"""
            CREATE TABLE IF NOT EXISTS {_History(schema)} (
                feature TEXT NOT NULL,
                step_version TEXT NOT NULL,
                description TEXT NOT NULL,
                checksum TEXT NOT NULL,
                applied_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%d %H:%M:%f', 'now') || '000+00:00'),
                CONSTRAINT "pk_{SchemaRunner.HistoryTableName}" PRIMARY KEY (feature, step_version)
            );
            """;
    }

    /// <inheritdoc />
    /// <remarks>The schema is a prefix of the name, as <see cref="SqliteDialect" /> qualifies names.</remarks>
    public string HistoryTableName(string schema)
    {
        return SqliteDialect.QualifiedName(schema, SchemaRunner.HistoryTableName);
    }

    /// <inheritdoc />
    public string ReadHistorySql(string schema)
    {
        return $"""
            SELECT feature, step_version, checksum
            FROM {_History(schema)}
            ORDER BY applied_at, feature, step_version;
            """;
    }

    /// <inheritdoc />
    public string InsertHistorySql(string schema)
    {
        return $"""
            INSERT INTO {_History(schema)} (feature, step_version, description, checksum)
            VALUES (@Feature, @StepVersion, @Description, @Checksum)
            ON CONFLICT (feature, step_version) DO NOTHING;
            """;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Never matches: a runner step's transaction holds the database write lock from its first statement, so a foreign
    /// creator has either committed before it, and the step's guards see the object, or waits for it.
    /// </remarks>
    public bool IsAlreadyCreatedRace(Exception exception)
    {
        return false;
    }

    /// <inheritdoc />
    /// <remarks>SQLite reports a missing table only through the message of a generic error.</remarks>
    public bool IsObjectNotFound(Exception exception)
    {
        return exception is SqliteException { SqliteErrorCode: SqliteErrors.Error } e
            && e.Message.Contains("no such table", StringComparison.Ordinal);
    }

    private string _History(string schema)
    {
        return SqliteDialect.Instance.Quote(HistoryTableName(schema));
    }
}
