// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.SqlServer;

/// <summary>
/// SQL Server implementation of <see cref="IStorageInitializer"/> for database schema setup.
/// Creates required tables (published, received, lock) and indexes on first run.
/// </summary>
public sealed class SqlServerStorageInitializer(
    ILogger<SqlServerStorageInitializer> logger,
    IOptions<SqlServerOptions> options,
    IOptions<MessagingOptions> messagingOptions
) : IStorageInitializer
{
    public string GetPublishedTableName()
    {
        return $"{options.Value.Schema}.Published";
    }

    public string GetReceivedTableName()
    {
        return $"{options.Value.Schema}.Received";
    }

    public string GetLockTableName()
    {
        return $"{options.Value.Schema}.Lock";
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var sql = _CreateDbTablesScript(options.Value.Schema);
        await using var connection = new SqlConnection(options.Value.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Only include lock parameters if UseStorageLock is enabled
        var sqlParams = new List<object>();
        if (messagingOptions.Value.UseStorageLock)
        {
            sqlParams.Add(new SqlParameter("@PubKey", $"publish_retry_{messagingOptions.Value.Version}"));
            sqlParams.Add(new SqlParameter("@RecKey", $"received_retry_{messagingOptions.Value.Version}"));
            sqlParams.Add(
                new SqlParameter("@LastLockTime", new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                {
                    SqlDbType = SqlDbType.DateTime2,
                }
            );
        }

        // Wrap the DDL batch in a transaction so a mid-script failure (network drop,
        // transient timeout) cannot leave the schema half-initialized. Each idempotent
        // block inside the script is also guarded by TRY/CATCH that swallows ONLY the
        // duplicate-object (2714) and PK-violation (2627) errors that fire under a TOCTOU
        // race between concurrent startups (e.g., 2+ pods rolling out simultaneously).
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await connection
            .ExecuteNonQueryAsync(sql, transaction, cancellationToken, sqlParams.AsArray())
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        logger.LogEnsuringTablesCreated();
    }

    private string _CreateDbTablesScript(string schema)
    {
        // Use underscore instead of period in constraint/index names for Azure SQL Edge compatibility
        var receivedPrefix = $"{schema}_Received";
        var publishedPrefix = $"{schema}_Published";
        var lockPrefix = $"{schema}_Lock";

        // Simplified SQL for Azure SQL Edge compatibility (no TEXTIMAGE_ON, simpler index options).
        // Each idempotent block is wrapped in BEGIN TRY ... BEGIN CATCH to absorb the narrow set of
        // duplicate-object/duplicate-key errors that fire only under a TOCTOU race between concurrent
        // initializers (e.g., simultaneous pod startup). Any other error is rethrown.
        //   2714 — "There is already an object named '...' in the database." (schema/table races)
        //   2627 — "Violation of PRIMARY KEY constraint." (lock-row INSERT races)
        var batchSql = $"""
            BEGIN TRY
                IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{schema}')
                BEGIN
                	EXEC('CREATE SCHEMA [{schema}]');
                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() <> 2714 THROW;
            END CATCH;

            BEGIN TRY
                IF OBJECT_ID(N'{GetReceivedTableName()}',N'U') IS NULL
                BEGIN
                    CREATE TABLE {GetReceivedTableName()}(
                        [Id] [bigint] NOT NULL,
                        [Version] [nvarchar](20) NOT NULL,
                        [Name] [nvarchar](200) NOT NULL,
                        [Group] [nvarchar](200) NULL,
                        [Content] [nvarchar](max) NULL,
                        [Retries] [int] NOT NULL,
                        [Added] [datetime2](7) NOT NULL,
                        [ExpiresAt] [datetime2](7) NULL,
                        [NextRetryAt] [datetime2](7) NULL,
                        [StatusName] [nvarchar](50) NOT NULL,
                        [MessageId] [nvarchar](200) NOT NULL,
                        [ExceptionInfo] [nvarchar](max) NULL,
                        CONSTRAINT [PK_{receivedPrefix}] PRIMARY KEY CLUSTERED ([Id] ASC)
                    );

                    CREATE UNIQUE NONCLUSTERED INDEX [IX_{receivedPrefix}_MessageId_Group] ON {GetReceivedTableName()} ([MessageId] ASC, [Group] ASC);
                    CREATE NONCLUSTERED INDEX [IX_{receivedPrefix}_Version_ExpiresAt_StatusName] ON {GetReceivedTableName()} ([Version] ASC,[ExpiresAt] ASC,[StatusName] ASC);
                    CREATE NONCLUSTERED INDEX [IX_{receivedPrefix}_ExpiresAt_StatusName] ON {GetReceivedTableName()} ([ExpiresAt] ASC,[StatusName] ASC);
                    CREATE NONCLUSTERED INDEX [IX_{receivedPrefix}_NextRetry] ON {GetReceivedTableName()} ([NextRetryAt] ASC) INCLUDE ([Version],[Retries]) WHERE [NextRetryAt] IS NOT NULL;
                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() <> 2714 THROW;
            END CATCH;

            BEGIN TRY
                IF OBJECT_ID(N'{GetPublishedTableName()}',N'U') IS NULL
                BEGIN
                    CREATE TABLE {GetPublishedTableName()}(
                        [Id] [bigint] NOT NULL,
                        [Version] [nvarchar](20) NOT NULL,
                        [Name] [nvarchar](200) NOT NULL,
                        [Content] [nvarchar](max) NULL,
                        [Retries] [int] NOT NULL,
                        [Added] [datetime2](7) NOT NULL,
                        [ExpiresAt] [datetime2](7) NULL,
                        [NextRetryAt] [datetime2](7) NULL,
                        [StatusName] [nvarchar](50) NOT NULL,
                        [MessageId] [nvarchar](200) NOT NULL,
                        CONSTRAINT [PK_{publishedPrefix}] PRIMARY KEY CLUSTERED ([Id] ASC)
                    );

                    CREATE NONCLUSTERED INDEX [IX_{publishedPrefix}_Version_ExpiresAt_StatusName] ON {GetPublishedTableName()} ([Version] ASC,[ExpiresAt] ASC,[StatusName] ASC);
                    CREATE NONCLUSTERED INDEX [IX_{publishedPrefix}_ExpiresAt_StatusName] ON {GetPublishedTableName()} ([ExpiresAt] ASC,[StatusName] ASC);
                    CREATE NONCLUSTERED INDEX [IX_{publishedPrefix}_NextRetry] ON {GetPublishedTableName()} ([NextRetryAt] ASC) INCLUDE ([Version],[Retries]) WHERE [NextRetryAt] IS NOT NULL;
                END;
            END TRY
            BEGIN CATCH
                IF ERROR_NUMBER() <> 2714 THROW;
            END CATCH;

""";

        if (messagingOptions.Value.UseStorageLock)
        {
            batchSql += $"""
                BEGIN TRY
                    IF OBJECT_ID(N'{GetLockTableName()}',N'U') IS NULL
                    BEGIN
                        CREATE TABLE {GetLockTableName()}(
                            [Key] [nvarchar](128) NOT NULL,
                            [Instance] [nvarchar](256) NOT NULL,
                            [LastLockTime] [datetime2](7) NOT NULL,
                            CONSTRAINT [PK_{lockPrefix}] PRIMARY KEY CLUSTERED ([Key] ASC)
                        );
                    END;
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() <> 2714 THROW;
                END CATCH;

                BEGIN TRY
                    IF NOT EXISTS (SELECT 1 FROM {GetLockTableName()} WHERE [Key] = @PubKey)
                        INSERT INTO {GetLockTableName()} ([Key],[Instance],[LastLockTime]) VALUES(@PubKey,'',@LastLockTime);
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() <> 2627 THROW;
                END CATCH;

                BEGIN TRY
                    IF NOT EXISTS (SELECT 1 FROM {GetLockTableName()} WHERE [Key] = @RecKey)
                        INSERT INTO {GetLockTableName()} ([Key],[Instance],[LastLockTime]) VALUES(@RecKey,'',@LastLockTime);
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() <> 2627 THROW;
                END CATCH;
                """;
        }

        return batchSql;
    }
}
