// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Messaging.PostgreSql;

public sealed class PostgreSqlStorageInitializer(
    ILogger<PostgreSqlStorageInitializer> logger,
    IOptions<PostgreSqlOptions> postgreSqlOptions,
    IOptions<MessagingOptions> messagingOptions
) : IStorageInitializer
{
    private readonly ILogger _logger = logger;

    public string GetPublishedTableName()
    {
        return $"\"{postgreSqlOptions.Value.Schema}\".\"published\"";
    }

    public string GetReceivedTableName()
    {
        return $"\"{postgreSqlOptions.Value.Schema}\".\"received\"";
    }

    public string GetLockTableName()
    {
        return $"\"{postgreSqlOptions.Value.Schema}\".\"lock\"";
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var sql = _CreateDbTablesScript(postgreSqlOptions.Value.Schema);
        await using var connection = postgreSqlOptions.Value.CreateConnection();

        object[] sqlParams =
        [
            new NpgsqlParameter("@PubKey", $"publish_retry_{messagingOptions.Value.Version}"),
            new NpgsqlParameter("@RecKey", $"received_retry_{messagingOptions.Value.Version}"),
            new NpgsqlParameter("@LastLockTime", DateTime.MinValue),
        ];

        await connection
            .ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken, sqlParams: sqlParams)
            .AnyContext();

        _logger.LogDebug("Ensuring all create database tables script are applied.");
    }

    private string _CreateDbTablesScript(string schema)
    {
        var batchSql = $"""
            CREATE SCHEMA IF NOT EXISTS "{schema}";

            CREATE TABLE IF NOT EXISTS {GetReceivedTableName()}(
            	"Id" BIGINT PRIMARY KEY NOT NULL,
                "Version" VARCHAR(20) NOT NULL,
            	"Name" VARCHAR(200) NOT NULL,
            	"Group" VARCHAR(200) NULL,
            	"Content" TEXT NULL,
            	"Retries" INT NOT NULL,
            	"Added" TIMESTAMP NOT NULL,
                "ExpiresAt" TIMESTAMP NULL,
            	"StatusName" VARCHAR(50) NOT NULL,
                "MessageId" VARCHAR(200) NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "idx_received_MessageId_Group" ON {GetReceivedTableName()} ("MessageId","Group");
            CREATE INDEX IF NOT EXISTS "idx_received_ExpiresAt_StatusName" ON {GetReceivedTableName()} ("ExpiresAt","StatusName");
            CREATE INDEX IF NOT EXISTS "idx_received_Version_ExpiresAt_StatusName" ON {GetReceivedTableName()} ("Version","ExpiresAt","StatusName");
            CREATE INDEX IF NOT EXISTS "idx_received_retry" ON {GetReceivedTableName()} ("StatusName","Retries","Added") WHERE "StatusName" IN ('Failed','Scheduled');
            CREATE INDEX IF NOT EXISTS "idx_received_delayed" ON {GetReceivedTableName()} ("StatusName","ExpiresAt") WHERE "StatusName" = 'Delayed';

            CREATE TABLE IF NOT EXISTS {GetPublishedTableName()}(
            	"Id" BIGINT PRIMARY KEY NOT NULL,
                "Version" VARCHAR(20) NOT NULL,
            	"Name" VARCHAR(200) NOT NULL,
            	"Content" TEXT NULL,
            	"Retries" INT NOT NULL,
            	"Added" TIMESTAMP NOT NULL,
                "ExpiresAt" TIMESTAMP NULL,
            	"StatusName" VARCHAR(50) NOT NULL
            );

            CREATE INDEX IF NOT EXISTS "idx_published_ExpiresAt_StatusName" ON {GetPublishedTableName()}("ExpiresAt","StatusName");
            CREATE INDEX IF NOT EXISTS "idx_published_Version_ExpiresAt_StatusName" ON {GetPublishedTableName()} ("Version","ExpiresAt","StatusName");
            CREATE INDEX IF NOT EXISTS "idx_published_retry" ON {GetPublishedTableName()} ("StatusName","Retries","Added") WHERE "StatusName" IN ('Failed','Scheduled');
            CREATE INDEX IF NOT EXISTS "idx_published_delayed" ON {GetPublishedTableName()} ("StatusName","ExpiresAt") WHERE "StatusName" = 'Delayed';
            """;

        if (messagingOptions.Value.UseStorageLock)
        {
            batchSql += $"""
                CREATE TABLE IF NOT EXISTS {GetLockTableName()}(
                	"Key" VARCHAR(128) PRIMARY KEY NOT NULL,
                    "Instance" VARCHAR(256),
                	"LastLockTime" TIMESTAMP NOT NULL
                );
                INSERT INTO {GetLockTableName()} ("Key","Instance","LastLockTime") VALUES(@PubKey,'',@LastLockTime) ON CONFLICT DO NOTHING;
                INSERT INTO {GetLockTableName()} ("Key","Instance","LastLockTime") VALUES(@RecKey,'',@LastLockTime) ON CONFLICT DO NOTHING;
                """;
        }

        return batchSql;
    }
}
