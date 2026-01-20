// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Framework.Messages.Configuration;
using Framework.Messages.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Messages;

public sealed class SqlServerStorageInitializer(
    ILogger<SqlServerStorageInitializer> logger,
    IOptions<SqlServerOptions> options,
    IOptions<MessagingOptions> messagingOptions
) : IStorageInitializer
{
    private readonly ILogger _logger = logger;

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
        var connection = new SqlConnection(options.Value.ConnectionString);
        await using var _ = connection;
        object[] sqlParams =
        [
            new SqlParameter("@PubKey", $"publish_retry_{messagingOptions.Value.Version}"),
            new SqlParameter("@RecKey", $"received_retry_{messagingOptions.Value.Version}"),
            new SqlParameter("@LastLockTime", DateTime.MinValue) { SqlDbType = SqlDbType.DateTime2 },
        ];
        await connection.ExecuteNonQueryAsync(sql, sqlParams: sqlParams).AnyContext();

        _logger.LogDebug("Ensuring all create database tables script are applied.");
    }

    private string _CreateDbTablesScript(string schema)
    {
        var batchSql = $"""
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{schema}')
            BEGIN
            	EXEC('CREATE SCHEMA [{schema}]')
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
             CONSTRAINT [PK_{GetReceivedTableName()}] PRIMARY KEY CLUSTERED
            (
            	[Id] ASC
            )WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
            ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

            CREATE UNIQUE NONCLUSTERED INDEX [IX_{GetReceivedTableName()}_MessageId_Group] ON {GetReceivedTableName()} ([MessageId] ASC, [Group] ASC)

            CREATE NONCLUSTERED INDEX [IX_{GetReceivedTableName()}_Version_ExpiresAt_StatusName] ON {GetReceivedTableName()} ([Version] ASC,[ExpiresAt] ASC,[StatusName] ASC)
            INCLUDE ([Id], [Content], [Retries], [Added])

            CREATE NONCLUSTERED INDEX [IX_{GetReceivedTableName()}_ExpiresAt_StatusName] ON {GetReceivedTableName()} ([ExpiresAt] ASC,[StatusName] ASC)

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
             CONSTRAINT [PK_{GetPublishedTableName()}] PRIMARY KEY CLUSTERED
            (
            	[Id] ASC
            )WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
            ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

            CREATE NONCLUSTERED INDEX [IX_{GetPublishedTableName()}_Version_ExpiresAt_StatusName] ON {GetPublishedTableName()} ([Version] ASC,[ExpiresAt] ASC,[StatusName] ASC)
            INCLUDE ([Id], [Content], [Retries], [Added])

            CREATE NONCLUSTERED INDEX [IX_{GetPublishedTableName()}_ExpiresAt_StatusName] ON {GetPublishedTableName()} ([ExpiresAt] ASC,[StatusName] ASC)

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
                 CONSTRAINT [PK_{GetLockTableName()}] PRIMARY KEY CLUSTERED
                (
                	[Key] ASC
                )WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = ON, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
                ) ON [PRIMARY]
                END;

                INSERT INTO {GetLockTableName()} ([Key],[Instance],[LastLockTime]) VALUES(@PubKey,'',@LastLockTime);
                INSERT INTO {GetLockTableName()} ([Key],[Instance],[LastLockTime]) VALUES(@RecKey,'',@LastLockTime);
                """;
        }

        return batchSql;
    }
}
