// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Checks;
using Headless.Hosting.Initialization.Schema;
using Microsoft.Data.SqlClient;

namespace Headless.Sql.SqlServer;

/// <summary>
/// The SQL Server dialect of the Headless schema runner: a session <c>sp_getapplock</c> per database, polled with a
/// zero timeout, and the <c>SqlException</c> numbers that mean another creator committed first.
/// </summary>
[PublicAPI]
public sealed class SqlServerSchemaDialect : ISchemaDialect
{
    /// <summary>The shared instance. The dialect holds no state.</summary>
    public static SqlServerSchemaDialect Instance { get; } = new();

    /// <inheritdoc />
    public string Name => "SqlServer";

    /// <inheritdoc />
    /// <remarks><c>sqlcmd</c> and SSMS split batches on <c>GO</c>.</remarks>
    public string? ScriptBatchSeparator => "GO";

    /// <inheritdoc />
    public string TryAcquireLockSql =>
        """
            DECLARE @lockResult int;
            EXEC @lockResult = sys.sp_getapplock
                @Resource = @LockResource,
                @LockMode = N'Exclusive',
                @LockOwner = N'Session',
                @LockTimeout = 0,
                @DbPrincipal = N'public';
            SELECT CASE WHEN @lockResult >= 0 THEN 1 ELSE 0 END;
            """;

    /// <inheritdoc />
    public string ReleaseLockSql =>
        """
            IF APPLOCK_MODE(N'public', @LockResource, N'Session') <> N'NoLock'
                EXEC sys.sp_releaseapplock @Resource = @LockResource, @LockOwner = N'Session', @DbPrincipal = N'public';
            """;

    /// <inheritdoc />
    public string DatabaseIdentity(DbConnection connection)
    {
        Argument.IsNotNull(connection);

        // Server and catalog only: pooling, timeouts, and credentials do not change which database is reached.
        var builder = new SqlConnectionStringBuilder(connection.ConnectionString);

        return $"{builder.DataSource}/{builder.InitialCatalog}".ToUpperInvariant();
    }

    /// <inheritdoc />
    public DbParameter CreateStringParameter(string name, string value)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsNotNull(value);

        return new SqlParameter(name, SqlDbType.NVarChar, Math.Max(1, value.Length)) { Value = value };
    }

    /// <inheritdoc />
    public string HistoryTableSql(string schema)
    {
        var table = $"[{schema}].[{SchemaRunner.HistoryTableName}]";

        // IF NOT EXISTS and the CREATE after it are not atomic against a creator outside the runner's lock, so the
        // create can still fail with 2714; the runner absorbs that and re-runs this batch once.
        return $"""
            IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'{schema}')
                EXEC(N'CREATE SCHEMA [{schema}]');

            IF OBJECT_ID(N'{table}', N'U') IS NULL
                CREATE TABLE {table} (
                    [Feature] nvarchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    [StepVersion] nvarchar(50) COLLATE Latin1_General_100_BIN2 NOT NULL,
                    [Description] nvarchar(500) NOT NULL,
                    [Checksum] char(64) NOT NULL,
                    [AppliedAt] datetimeoffset(7) NOT NULL CONSTRAINT [DF_{SchemaRunner.HistoryTableName}_AppliedAt] DEFAULT TODATETIMEOFFSET(SYSUTCDATETIME(), 0),
                    CONSTRAINT [PK_{SchemaRunner.HistoryTableName}] PRIMARY KEY CLUSTERED ([Feature], [StepVersion])
                );
            """;
    }

    /// <inheritdoc />
    public string HistoryTableName(string schema)
    {
        return SchemaRunner.HistoryTableName;
    }

    /// <inheritdoc />
    public string ReadHistorySql(string schema)
    {
        return $"""
            SELECT [Feature], [StepVersion], [Checksum]
            FROM [{schema}].[{SchemaRunner.HistoryTableName}]
            ORDER BY [AppliedAt], [Feature], [StepVersion];
            """;
    }

    /// <inheritdoc />
    public string InsertHistorySql(string schema)
    {
        var table = $"[{schema}].[{SchemaRunner.HistoryTableName}]";

        return $"""
            IF NOT EXISTS (SELECT 1 FROM {table} WHERE [Feature] = @Feature AND [StepVersion] = @StepVersion)
                INSERT INTO {table} ([Feature], [StepVersion], [Description], [Checksum])
                VALUES (@Feature, @StepVersion, @Description, @Checksum);
            """;
    }

    /// <inheritdoc />
    /// <remarks>2714 (object exists), 1913 (index exists), 2759 (<c>CREATE SCHEMA</c> duplicate).</remarks>
    public bool IsAlreadyCreatedRace(Exception exception)
    {
        return exception is SqlException { Number: 2714 or 1913 or 2759 };
    }

    /// <inheritdoc />
    /// <remarks>208 (invalid object name) covers both a missing table and a missing schema.</remarks>
    public bool IsObjectNotFound(Exception exception)
    {
        return exception is SqlException { Number: 208 };
    }
}
