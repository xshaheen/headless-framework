// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Messaging.PostgreSql;

/// <summary>
/// PostgreSQL implementation of <see cref="IStorageInitializer"/> for database schema setup.
/// Creates required tables (published, received, lock) and indexes on first run.
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
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Only include lock parameters if UseStorageLock is enabled. Npgsql throws at execute time
        // when parameters are present but the SQL has no matching placeholders — mirrors the existing
        // guard in SqlServerStorageInitializer.
        object[] sqlParams = messagingOptions.Value.UseStorageLock
            ?
            [
                new NpgsqlParameter("@PubKey", $"publish_retry_{messagingOptions.Value.Version}"),
                new NpgsqlParameter("@RecKey", $"received_retry_{messagingOptions.Value.Version}"),
                new NpgsqlParameter("@LastLockTime", DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)),
            ]
            : [];

        // PostgreSQL supports transactional DDL — wrap the batch so a mid-script failure
        // (network drop, broker-side abort) cannot leave the schema half-initialized.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await connection
            .ExecuteNonQueryAsync(
                sql,
                transaction: transaction,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: sqlParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        logger.LogEnsuringTablesCreated();
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
            	"Added" TIMESTAMPTZ NOT NULL,
                "ExpiresAt" TIMESTAMPTZ NULL,
                "NextRetryAt" TIMESTAMPTZ NULL,
                "LockedUntil" TIMESTAMPTZ NULL,
            	"StatusName" VARCHAR(50) NOT NULL,
                "MessageId" VARCHAR(200) NOT NULL,
                "ExceptionInfo" text NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "idx_received_MessageId_Group" ON {GetReceivedTableName()} ("MessageId","Group");
            -- NULL-safe upsert key. PostgreSQL treats NULL values as distinct in a multi-column
            -- unique index, so the plain ("MessageId","Group") index above does NOT prevent
            -- duplicate inserts when "Group" IS NULL (broker redelivery of a no-group message
            -- would accumulate rows). COALESCE("Group", '') collapses NULL into a sentinel so the
            -- INSERT ... ON CONFLICT path in PostgreSqlDataStorage._StoreReceivedMessage can name
            -- this index as its conflict target and converge concurrent inserts to a single row.
            CREATE UNIQUE INDEX IF NOT EXISTS "uq_received_MessageId_GroupCoalesced" ON {GetReceivedTableName()} ("MessageId", (COALESCE("Group", '')));
            CREATE INDEX IF NOT EXISTS "idx_received_ExpiresAt_StatusName" ON {GetReceivedTableName()} ("ExpiresAt","StatusName");
            CREATE INDEX IF NOT EXISTS "idx_received_Version_ExpiresAt_StatusName" ON {GetReceivedTableName()} ("Version","ExpiresAt","StatusName");
            -- Partial index for retry pickup. Keyed on (Version, NextRetryAt) so Version is a seek
            -- predicate rather than a residual filter — the pickup query filters on both. Includes
            -- Retries AND LockedUntil so the lease predicate `(LockedUntil IS NULL OR LockedUntil <= @Now)`
            -- can be evaluated from the index without a per-candidate heap fetch. Index-only scan
            -- requires healthy autovacuum so the visibility map covers the relation; under heavy
            -- write load the planner may fall back to heap fetches bounded by the retry batch size.
            --
            -- Conditional recreate (R4): mirror SqlServerStorageInitializer's gated DROP/CREATE so
            -- the AccessExclusiveLock is only taken when the existing index is missing the
            -- LockedUntil column from its INCLUDE list. Unconditional DROP/CREATE on every startup
            -- would lock the hot retry-pickup index on every boot.
            DO $do$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM pg_indexes
                    WHERE schemaname = '{schema}' AND indexname = 'idx_received_Version_NextRetryAt'
                ) AND NOT EXISTS (
                    -- Check ALL columns the index covers (key + INCLUDE), not just key columns.
                    -- Querying pg_attribute against the index relation's OID (c.oid) returns every
                    -- column physically stored in the index, which is what we need to detect
                    -- LockedUntil in the INCLUDE list.
                    SELECT 1 FROM pg_index i
                    JOIN pg_class c ON c.oid = i.indexrelid
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    JOIN pg_attribute a ON a.attrelid = c.oid
                    WHERE n.nspname = '{schema}'
                      AND c.relname = 'idx_received_Version_NextRetryAt'
                      AND a.attname = 'LockedUntil'
                ) THEN
                    EXECUTE 'DROP INDEX IF EXISTS "{schema}"."idx_received_Version_NextRetryAt" CASCADE';
                END IF;
            END $do$;
            CREATE INDEX IF NOT EXISTS "idx_received_Version_NextRetryAt" ON {GetReceivedTableName()} ("Version","NextRetryAt") INCLUDE ("Retries","LockedUntil") WHERE "NextRetryAt" IS NOT NULL;
            CREATE INDEX IF NOT EXISTS "idx_received_delayed" ON {GetReceivedTableName()} ("StatusName","ExpiresAt") WHERE "StatusName" = 'Delayed';

            CREATE TABLE IF NOT EXISTS {GetPublishedTableName()}(
            	"Id" BIGINT PRIMARY KEY NOT NULL,
                "Version" VARCHAR(20) NOT NULL,
            	"Name" VARCHAR(200) NOT NULL,
            	"Content" TEXT NULL,
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
            -- Partial index for retry pickup. Keyed on (Version, NextRetryAt) so Version is a seek
            -- predicate rather than a residual filter — the pickup query filters on both. Includes
            -- Retries AND LockedUntil so the lease predicate `(LockedUntil IS NULL OR LockedUntil <= @Now)`
            -- can be evaluated from the index without a per-candidate heap fetch. Index-only scan
            -- requires healthy autovacuum so the visibility map covers the relation; under heavy
            -- write load the planner may fall back to heap fetches bounded by the retry batch size.
            --
            -- Conditional recreate (R4): see the matching comment on the received-table block above.
            DO $do$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM pg_indexes
                    WHERE schemaname = '{schema}' AND indexname = 'idx_published_Version_NextRetryAt'
                ) AND NOT EXISTS (
                    -- See the matching pg_attribute join on the received-index block above for why
                    -- we query against c.oid (the index relation) rather than against indrelid+indkey.
                    SELECT 1 FROM pg_index i
                    JOIN pg_class c ON c.oid = i.indexrelid
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    JOIN pg_attribute a ON a.attrelid = c.oid
                    WHERE n.nspname = '{schema}'
                      AND c.relname = 'idx_published_Version_NextRetryAt'
                      AND a.attname = 'LockedUntil'
                ) THEN
                    EXECUTE 'DROP INDEX IF EXISTS "{schema}"."idx_published_Version_NextRetryAt" CASCADE';
                END IF;
            END $do$;
            CREATE INDEX IF NOT EXISTS "idx_published_Version_NextRetryAt" ON {GetPublishedTableName()} ("Version","NextRetryAt") INCLUDE ("Retries","LockedUntil") WHERE "NextRetryAt" IS NOT NULL;
            CREATE INDEX IF NOT EXISTS "idx_published_delayed" ON {GetPublishedTableName()} ("StatusName","ExpiresAt") WHERE "StatusName" = 'Delayed';
            """;

        if (messagingOptions.Value.UseStorageLock)
        {
            batchSql += $"""
                CREATE TABLE IF NOT EXISTS {GetLockTableName()}(
                	"Key" VARCHAR(128) PRIMARY KEY NOT NULL,
                    "Instance" VARCHAR(256),
                	"LastLockTime" TIMESTAMPTZ NOT NULL
                );
                INSERT INTO {GetLockTableName()} ("Key","Instance","LastLockTime") VALUES(@PubKey,'',@LastLockTime) ON CONFLICT DO NOTHING;
                INSERT INTO {GetLockTableName()} ("Key","Instance","LastLockTime") VALUES(@RecKey,'',@LastLockTime) ON CONFLICT DO NOTHING;
                """;
        }

        return batchSql;
    }
}
