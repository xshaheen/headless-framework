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

        await connection
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: sqlParams.AsArray())
            .AnyContext();

        logger.LogDebug("Ensuring all create database tables script are applied.");
    }

    private string _CreateDbTablesScript(string schema)
    {
        // Use underscore instead of period in constraint/index names for Azure SQL Edge compatibility
        var receivedPrefix = $"{schema}_Received";
        var publishedPrefix = $"{schema}_Published";
        var lockPrefix = $"{schema}_Lock";

        // Simplified SQL for Azure SQL Edge compatibility (no TEXTIMAGE_ON, simpler index options)
        var batchSql = $"""
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{schema}')
            BEGIN
            	EXEC('CREATE SCHEMA [{schema}]');
            END;

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
                    [StatusName] [nvarchar](50) NOT NULL,
                    [MessageId] [nvarchar](200) NOT NULL,
                    CONSTRAINT [PK_{receivedPrefix}] PRIMARY KEY CLUSTERED ([Id] ASC)
                );

                CREATE UNIQUE NONCLUSTERED INDEX [IX_{receivedPrefix}_MessageId_Group] ON {GetReceivedTableName()} ([MessageId] ASC, [Group] ASC);
                CREATE NONCLUSTERED INDEX [IX_{receivedPrefix}_Version_ExpiresAt_StatusName] ON {GetReceivedTableName()} ([Version] ASC,[ExpiresAt] ASC,[StatusName] ASC);
                CREATE NONCLUSTERED INDEX [IX_{receivedPrefix}_ExpiresAt_StatusName] ON {GetReceivedTableName()} ([ExpiresAt] ASC,[StatusName] ASC);
                CREATE NONCLUSTERED INDEX [IX_{receivedPrefix}_RetryQuery] ON {GetReceivedTableName()} ([Version] ASC,[StatusName] ASC,[Retries] ASC,[Added] ASC);
            END;

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
                    [StatusName] [nvarchar](50) NOT NULL,
                    CONSTRAINT [PK_{publishedPrefix}] PRIMARY KEY CLUSTERED ([Id] ASC)
                );

                CREATE NONCLUSTERED INDEX [IX_{publishedPrefix}_Version_ExpiresAt_StatusName] ON {GetPublishedTableName()} ([Version] ASC,[ExpiresAt] ASC,[StatusName] ASC);
                CREATE NONCLUSTERED INDEX [IX_{publishedPrefix}_ExpiresAt_StatusName] ON {GetPublishedTableName()} ([ExpiresAt] ASC,[StatusName] ASC);
                CREATE NONCLUSTERED INDEX [IX_{publishedPrefix}_RetryQuery] ON {GetPublishedTableName()} ([Version] ASC,[StatusName] ASC,[Retries] ASC,[Added] ASC);
            END;

            """;

        if (messagingOptions.Value.UseStorageLock)
        {
            batchSql += $"""
                IF OBJECT_ID(N'{GetLockTableName()}',N'U') IS NULL
                BEGIN
                    CREATE TABLE {GetLockTableName()}(
                        [Key] [nvarchar](128) NOT NULL,
                        [Instance] [nvarchar](256) NOT NULL,
                        [LastLockTime] [datetime2](7) NOT NULL,
                        CONSTRAINT [PK_{lockPrefix}] PRIMARY KEY CLUSTERED ([Key] ASC)
                    );
                END;

                IF NOT EXISTS (SELECT 1 FROM {GetLockTableName()} WHERE [Key] = @PubKey)
                    INSERT INTO {GetLockTableName()} ([Key],[Instance],[LastLockTime]) VALUES(@PubKey,'',@LastLockTime);
                IF NOT EXISTS (SELECT 1 FROM {GetLockTableName()} WHERE [Key] = @RecKey)
                    INSERT INTO {GetLockTableName()} ([Key],[Instance],[LastLockTime]) VALUES(@RecKey,'',@LastLockTime);
                """;
        }

        return batchSql;
    }
}
