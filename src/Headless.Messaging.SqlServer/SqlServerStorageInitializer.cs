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

    public string GetScheduledJobsTableName()
    {
        return $"{options.Value.Schema}.ScheduledJobs";
    }

    public string GetJobExecutionsTableName()
    {
        return $"{options.Value.Schema}.JobExecutions";
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
            .ConfigureAwait(false);

        logger.LogEnsuringTablesCreated();
    }

    private string _CreateDbTablesScript(string schema)
    {
        // Use underscore instead of period in constraint/index names for Azure SQL Edge compatibility
        var receivedPrefix = $"{schema}_Received";
        var publishedPrefix = $"{schema}_Published";
        var lockPrefix = $"{schema}_Lock";
        var scheduledPrefix = $"{schema}_ScheduledJobs";
        var executionsPrefix = $"{schema}_JobExecutions";

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

            IF OBJECT_ID(N'{GetScheduledJobsTableName()}',N'U') IS NULL
            BEGIN
                CREATE TABLE {GetScheduledJobsTableName()}(
                    [Id] [uniqueidentifier] NOT NULL,
                    [Name] [nvarchar](200) NOT NULL,
                    [Type] [nvarchar](20) NOT NULL,
                    [CronExpression] [nvarchar](100) NULL,
                    [TimeZone] [nvarchar](100) NOT NULL DEFAULT 'UTC',
                    [Payload] [nvarchar](max) NULL,
                    [Status] [nvarchar](50) NOT NULL,
                    [NextRunTime] [datetimeoffset](7) NULL,
                    [LastRunTime] [datetimeoffset](7) NULL,
                    [LastRunDuration] [bigint] NULL,
                    [MaxRetries] [int] NOT NULL DEFAULT 0,
                    [RetryIntervals] [nvarchar](max) NULL,
                    [SkipIfRunning] [bit] NOT NULL DEFAULT 1,
                    [LockHolder] [nvarchar](256) NULL,
                    [DateLocked] [datetimeoffset](7) NULL,
                    [IsEnabled] [bit] NOT NULL DEFAULT 1,
                    [DateCreated] [datetimeoffset](7) NOT NULL,
                    [DateUpdated] [datetimeoffset](7) NOT NULL,
                    [Timeout] [bigint] NULL,
                    [MisfireStrategy] [nvarchar](50) NOT NULL DEFAULT 'FireImmediately',
                    [ConsumerTypeName] [nvarchar](500) NULL,
                    [Version] [bigint] NOT NULL DEFAULT 0,
                    CONSTRAINT [PK_{scheduledPrefix}] PRIMARY KEY CLUSTERED ([Id] ASC)
                );

                CREATE UNIQUE NONCLUSTERED INDEX [IX_{scheduledPrefix}_Name] ON {GetScheduledJobsTableName()} ([Name] ASC);
                CREATE NONCLUSTERED INDEX [IX_{scheduledPrefix}_NextRun] ON {GetScheduledJobsTableName()} ([NextRunTime] ASC)
                    WHERE [Status] = 'Pending' AND [IsEnabled] = 1;
                CREATE NONCLUSTERED INDEX [IX_{scheduledPrefix}_Lock] ON {GetScheduledJobsTableName()} ([LockHolder] ASC, [DateLocked] ASC)
                    WHERE [Status] = 'Running';
            END;

            IF OBJECT_ID(N'{GetJobExecutionsTableName()}',N'U') IS NULL
            BEGIN
                CREATE TABLE {GetJobExecutionsTableName()}(
                    [Id] [uniqueidentifier] NOT NULL,
                    [JobId] [uniqueidentifier] NOT NULL,
                    [ScheduledTime] [datetimeoffset](7) NOT NULL,
                    [DateStarted] [datetimeoffset](7) NULL,
                    [DateCompleted] [datetimeoffset](7) NULL,
                    [Status] [nvarchar](50) NOT NULL,
                    [Duration] [bigint] NULL,
                    [RetryAttempt] [int] NOT NULL DEFAULT 0,
                    [Error] [nvarchar](max) NULL,
                    CONSTRAINT [PK_{executionsPrefix}] PRIMARY KEY CLUSTERED ([Id] ASC),
                    CONSTRAINT [FK_{executionsPrefix}_JobId] FOREIGN KEY ([JobId])
                        REFERENCES {GetScheduledJobsTableName()}([Id]) ON DELETE CASCADE
                );

                CREATE NONCLUSTERED INDEX [IX_{executionsPrefix}_JobId] ON {GetJobExecutionsTableName()} ([JobId] ASC);
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
