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

        // #8 — Retry-pickup partial indexes use CREATE INDEX CONCURRENTLY so the AccessExclusiveLock
        // is replaced with a ShareUpdateExclusiveLock — readers and writers stay live during the
        // rebuild. CONCURRENTLY cannot run inside a transaction (PG raises 25001), so these run on
        // an autocommit connection AFTER the schema/table DDL has committed above. Each statement is
        // standalone (no transaction wrapping); CREATE INDEX CONCURRENTLY's own internal scan handles
        // crash recovery via an INVALID index that is dropped on a subsequent initialize pass.
        await _EnsureRetryPickupIndexConcurrentlyAsync(
                connection,
                postgreSqlOptions.Value.Schema,
                GetReceivedTableName(),
                indexName: "idx_received_Version_NextRetryAt",
                cancellationToken
            )
            .ConfigureAwait(false);

        await _EnsureRetryPickupIndexConcurrentlyAsync(
                connection,
                postgreSqlOptions.Value.Schema,
                GetPublishedTableName(),
                indexName: "idx_published_Version_NextRetryAt",
                cancellationToken
            )
            .ConfigureAwait(false);

        logger.LogEnsuringTablesCreated();
    }

    private async Task _EnsureRetryPickupIndexConcurrentlyAsync(
        NpgsqlConnection connection,
        string schema,
        string qualifiedTable,
        string indexName,
        CancellationToken cancellationToken
    )
    {
        // Detect the older-shape index (missing LockedUntil from INCLUDE) so we only pay the
        // rebuild cost when the columns drifted. Mirrors the gated check in the in-transaction
        // script for the SchemaIfMissing path. When the existing index already includes
        // LockedUntil, this is a no-op.
        //
        // #12 — Also drop the index when pg_index.indisvalid = false. A CREATE INDEX CONCURRENTLY
        // that was aborted mid-build leaves the system catalog entry behind but with indisvalid=false;
        // the subsequent CREATE INDEX CONCURRENTLY IF NOT EXISTS is then a no-op, leaving an invalid
        // (and unusable) index in place. Dropping it forces the next CREATE to rebuild a healthy index.
        var dropOnDrift = $"""
            DO $do$
            DECLARE
                _has_locked_until BOOLEAN;
                _is_valid BOOLEAN;
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM pg_indexes
                    WHERE schemaname = '{schema}' AND indexname = '{indexName}'
                ) THEN
                    SELECT EXISTS (
                        SELECT 1 FROM pg_index i
                        JOIN pg_class c ON c.oid = i.indexrelid
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        JOIN pg_attribute a ON a.attrelid = c.oid
                        WHERE n.nspname = '{schema}'
                          AND c.relname = '{indexName}'
                          AND a.attname = 'LockedUntil'
                    ) INTO _has_locked_until;

                    SELECT COALESCE((
                        SELECT i.indisvalid FROM pg_index i
                        JOIN pg_class c ON c.oid = i.indexrelid
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = '{schema}' AND c.relname = '{indexName}'
                    ), TRUE) INTO _is_valid;

                    IF NOT _has_locked_until OR NOT _is_valid THEN
                        EXECUTE 'DROP INDEX CONCURRENTLY IF EXISTS "{schema}"."{indexName}"';
                    END IF;
                END IF;
            END $do$;
            """;

        // DROP INDEX CONCURRENTLY also cannot run inside a transaction, so the DO block uses
        // EXECUTE for the concurrent drop. Npgsql treats DO as a standalone statement when no
        // outer transaction is open.
        await connection
            .ExecuteNonQueryAsync(
                dropOnDrift,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        // CREATE INDEX CONCURRENTLY is idempotent via IF NOT EXISTS; the DROP above handles drift.
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
