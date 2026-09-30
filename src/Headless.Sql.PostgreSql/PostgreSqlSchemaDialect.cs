// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Constants;
using Headless.Hosting.Initialization.Schema;
using Npgsql;

namespace Headless.Sql.PostgreSql;

/// <summary>
/// The PostgreSQL dialect of the Headless schema runner: a session advisory lock per database, polled with
/// <c>pg_try_advisory_lock</c>, and the <c>PostgresException</c> states that mean another creator committed first.
/// </summary>
[PublicAPI]
public sealed class PostgreSqlSchemaDialect : ISchemaDialect
{
    /// <summary>The shared instance. The dialect holds no state.</summary>
    public static PostgreSqlSchemaDialect Instance { get; } = new();

    /// <inheritdoc />
    public string Name => "PostgreSql";

    /// <inheritdoc />
    /// <remarks><c>psql</c> runs one file as a single script, so no separator is emitted.</remarks>
    public string? ScriptBatchSeparator => null;

    /// <inheritdoc />
    public string TryAcquireLockSql => "SELECT pg_try_advisory_lock(hashtextextended(@LockResource, 0));";

    /// <inheritdoc />
    /// <remarks>Returns false rather than failing when the lock is not held.</remarks>
    public string ReleaseLockSql => "SELECT pg_advisory_unlock(hashtextextended(@LockResource, 0));";

    /// <inheritdoc />
    public string DatabaseIdentity(DbConnection connection)
    {
        Argument.IsNotNull(connection);

        // Host, port, and database only: pooling, timeouts, and credentials do not change which database is reached.
        // PostgreSQL folds unquoted names to lower case, so the identity does too.
        var builder = new NpgsqlConnectionStringBuilder(connection.ConnectionString);

        return $"{builder.Host}:{builder.Port}/{builder.Database}".ToLowerInvariant();
    }

    /// <inheritdoc />
    public DbParameter CreateStringParameter(string name, string value)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsNotNull(value);

        return new NpgsqlParameter(name, value);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Takes the schema-wide transaction lock every hand-written PostgreSQL initializer takes before
    /// <c>CREATE SCHEMA</c>, so the runner serializes against features not yet migrated onto it.
    /// </remarks>
    public string HistoryTableSql(string schema)
    {
        return $"""
            {PostgreSqlSchemaInitLock.AcquireStatement(schema)}
            CREATE SCHEMA IF NOT EXISTS "{schema}";

            CREATE TABLE IF NOT EXISTS "{schema}"."{SchemaRunner.HistoryTableName}" (
                feature varchar(200) COLLATE "C" NOT NULL,
                step_version varchar(50) COLLATE "C" NOT NULL,
                description varchar(500) NOT NULL,
                checksum char(64) NOT NULL,
                applied_at timestamptz NOT NULL DEFAULT clock_timestamp(),
                CONSTRAINT pk_{SchemaRunner.HistoryTableName} PRIMARY KEY (feature, step_version)
            );
            """;
    }

    /// <inheritdoc />
    public string ReadHistorySql(string schema)
    {
        return $"""
            SELECT feature, step_version, checksum
            FROM "{schema}"."{SchemaRunner.HistoryTableName}"
            ORDER BY applied_at, feature, step_version;
            """;
    }

    /// <inheritdoc />
    public string InsertHistorySql(string schema)
    {
        return $"""
            INSERT INTO "{schema}"."{SchemaRunner.HistoryTableName}" (feature, step_version, description, checksum)
            VALUES (@Feature, @StepVersion, @Description, @Checksum)
            ON CONFLICT (feature, step_version) DO NOTHING;
            """;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>42P06</c>/<c>42P07</c>/<c>42710</c> when the committed object is seen, and <c>23505</c> when the two catalog
    /// inserts raced on the catalog's unique index.
    /// </remarks>
    public bool IsAlreadyCreatedRace(Exception exception)
    {
        return exception
            is PostgresException
            {
                SqlState: SqlErrorCodes.PostgreSql.DuplicateSchema
                    or SqlErrorCodes.PostgreSql.DuplicateTable
                    or SqlErrorCodes.PostgreSql.DuplicateObject
                    or SqlErrorCodes.PostgreSql.UniqueViolation,
            };
    }

    /// <inheritdoc />
    /// <remarks><c>42P01</c> (undefined_table) covers both a missing table and a missing schema.</remarks>
    public bool IsObjectNotFound(Exception exception)
    {
        return exception is PostgresException { SqlState: "42P01" };
    }
}
