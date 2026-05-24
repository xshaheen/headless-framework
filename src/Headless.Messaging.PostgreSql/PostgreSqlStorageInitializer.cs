// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Messaging.PostgreSql;

/// <summary>
/// PostgreSQL implementation of <see cref="IStorageInitializer"/> for database schema setup.
/// Creates required tables (published, received) and indexes on first run.
/// </summary>
public sealed class PostgreSqlStorageInitializer(
    ILogger<PostgreSqlStorageInitializer> logger,
    IOptions<PostgreSqlOptions> postgreSqlOptions,
    IOptions<MessagingOptions> messagingOptions
) : IStorageInitializer
{
    public string GetPublishedTableName()
    {
        return $"\"{postgreSqlOptions.Value.Schema}\".\"published\"";
    }

    public string GetReceivedTableName()
    {
        return $"\"{postgreSqlOptions.Value.Schema}\".\"received\"";
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var sql = _CreateDbTablesScript(postgreSqlOptions.Value.Schema);
        await using var connection = postgreSqlOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // PostgreSQL supports transactional DDL — wrap the batch so a mid-script failure
        // (network drop, broker-side abort) cannot leave the schema half-initialized.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await connection
            .ExecuteNonQueryAsync(
                sql,
                transaction: transaction,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        // Retry-pickup partial indexes use CREATE INDEX CONCURRENTLY so the AccessExclusiveLock
        // is replaced with a ShareUpdateExclusiveLock — readers and writers stay live during the
        // create. CONCURRENTLY cannot run inside a transaction (PG raises 25001), so these run on
        // an autocommit connection AFTER the schema/table DDL has committed above. Each statement is
        // standalone (no transaction wrapping).
        await _EnsureRetryPickupIndexConcurrentlyAsync(
                connection,
                GetReceivedTableName(),
                indexName: "idx_received_Version_NextRetryAt",
                cancellationToken
            )
            .ConfigureAwait(false);

        await _EnsureRetryPickupIndexConcurrentlyAsync(
                connection,
                GetPublishedTableName(),
                indexName: "idx_published_Version_NextRetryAt",
                cancellationToken
            )
            .ConfigureAwait(false);

        logger.LogEnsuringTablesCreated();
    }

    private async Task _EnsureRetryPickupIndexConcurrentlyAsync(
        NpgsqlConnection connection,
        string qualifiedTable,
        string indexName,
        CancellationToken cancellationToken
    )
    {
        var createIndex = $"""
            CREATE INDEX CONCURRENTLY IF NOT EXISTS "{indexName}" ON {qualifiedTable} ("Version","NextRetryAt") INCLUDE ("Retries","LockedUntil") WHERE "NextRetryAt" IS NOT NULL;
            """;

        await connection
            .ExecuteNonQueryAsync(
                createIndex,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
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
                "IntentType" SMALLINT NOT NULL,
            	"Retries" INT NOT NULL,
            	"Added" TIMESTAMPTZ NOT NULL,
                "ExpiresAt" TIMESTAMPTZ NULL,
                "NextRetryAt" TIMESTAMPTZ NULL,
                "LockedUntil" TIMESTAMPTZ NULL,
            	"StatusName" VARCHAR(50) NOT NULL,
                "MessageId" VARCHAR(200) NOT NULL,
                "ExceptionInfo" text NULL
            );

            -- NULL-safe upsert key. PostgreSQL treats NULL values as distinct in a multi-column
            -- unique index, so a plain ("MessageId","Group") unique index does NOT prevent
            -- duplicate inserts when "Group" IS NULL (broker redelivery of a no-group message
            -- would accumulate rows). COALESCE("Group", '') collapses NULL into a sentinel so the
            -- INSERT ... ON CONFLICT path in PostgreSqlDataStorage._StoreReceivedMessage can name
            -- this index as its conflict target and converge concurrent inserts to a single row.
            --
            -- A second plain ("MessageId","Group") unique index is intentionally NOT created: when
            -- two unique indexes cover the same column set, PostgreSQL's choice of which one fires
            -- on a violation is non-deterministic. The plain index would fire first for non-null
            -- groups and produce a raw 23505 that bypasses the ON CONFLICT target, breaking
            -- concurrent-insert convergence under load.
            CREATE UNIQUE INDEX IF NOT EXISTS "uq_received_Version_MessageId_GroupCoalesced_IntentType" ON {GetReceivedTableName()} ("Version", "MessageId", (COALESCE("Group", '')), "IntentType");
            CREATE INDEX IF NOT EXISTS "idx_received_ExpiresAt_StatusName" ON {GetReceivedTableName()} ("ExpiresAt","StatusName");
            CREATE INDEX IF NOT EXISTS "idx_received_Version_ExpiresAt_StatusName" ON {GetReceivedTableName()} ("Version","ExpiresAt","StatusName");
            -- #8 — The partial retry-pickup index (idx_received_Version_NextRetryAt) is created
            -- post-transaction with CREATE INDEX CONCURRENTLY in _EnsureRetryPickupIndexConcurrentlyAsync.
            -- CREATE INDEX CONCURRENTLY cannot run inside a transaction; doing the create here would
            -- take an AccessExclusiveLock and block all writers to the hot retry-pickup path during
            -- every replica boot.
            CREATE INDEX IF NOT EXISTS "idx_received_delayed" ON {GetReceivedTableName()} ("StatusName","ExpiresAt") WHERE "StatusName" = 'Delayed';

            CREATE TABLE IF NOT EXISTS {GetPublishedTableName()}(
            	"Id" BIGINT PRIMARY KEY NOT NULL,
                "Version" VARCHAR(20) NOT NULL,
            	"Name" VARCHAR(200) NOT NULL,
            	"Content" TEXT NULL,
                "IntentType" SMALLINT NOT NULL,
            	"Retries" INT NOT NULL,
            	"Added" TIMESTAMPTZ NOT NULL,
                "ExpiresAt" TIMESTAMPTZ NULL,
                "NextRetryAt" TIMESTAMPTZ NULL,
                "LockedUntil" TIMESTAMPTZ NULL,
            	"StatusName" VARCHAR(50) NOT NULL,
                "MessageId" VARCHAR(200) NOT NULL
            );

            CREATE INDEX IF NOT EXISTS "idx_published_ExpiresAt_StatusName" ON {GetPublishedTableName()}("ExpiresAt","StatusName");
            CREATE INDEX IF NOT EXISTS "idx_published_Version_ExpiresAt_StatusName" ON {GetPublishedTableName()} ("Version","ExpiresAt","StatusName");
            -- #8 — see the matching comment on the received-table block above; the partial
            -- retry-pickup index for published is also created post-transaction via
            -- _EnsureRetryPickupIndexConcurrentlyAsync.
            CREATE INDEX IF NOT EXISTS "idx_published_delayed" ON {GetPublishedTableName()} ("StatusName","ExpiresAt") WHERE "StatusName" = 'Delayed';

            """;

        return batchSql;
    }
}
