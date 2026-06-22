// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Messaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of <see cref="IStorageInitializer"/> for database schema setup.
/// Creates required tables (published, received) and indexes on first run.
/// </summary>
internal sealed class PostgreSqlStorageInitializer(
    ILogger<PostgreSqlStorageInitializer> logger,
    IOptions<PostgreSqlOptions> postgreSqlOptions,
    IOptions<MessagingOptions> messagingOptions
) : IStorageInitializer
{
    /// <summary>
    /// Returns the fully-qualified PostgreSQL table name for published outbox messages,
    /// in the form <c>"schema"."published"</c>.
    /// </summary>
    public string GetPublishedTableName()
    {
        return $"\"{postgreSqlOptions.Value.Schema}\".\"published\"";
    }

    /// <summary>
    /// Returns the fully-qualified PostgreSQL table name for received outbox messages,
    /// in the form <c>"schema"."received"</c>.
    /// </summary>
    public string GetReceivedTableName()
    {
        return $"\"{postgreSqlOptions.Value.Schema}\".\"received\"";
    }

    /// <summary>
    /// Creates the messaging schema, tables, and indexes if they do not already exist.
    /// The core DDL runs inside a PostgreSQL transaction (DDL is transactional in PostgreSQL).
    /// Partial indexes for retry pickup and full-text content search are created with
    /// <c>CREATE INDEX CONCURRENTLY</c> after the transaction commits so writers remain
    /// unblocked during startup on hot tables.
    /// </summary>
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

        // Retry-pickup partial indexes and trigram content indexes use CREATE INDEX CONCURRENTLY so
        // the AccessExclusiveLock is replaced with a ShareUpdateExclusiveLock — readers and writers
        // stay live during the create. CONCURRENTLY cannot run inside a transaction (PG raises 25001),
        // so these run on an autocommit connection AFTER the schema/table DDL has committed above.
        // Each statement is standalone (no transaction wrapping).
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

        await _EnsureContentTrgmIndexConcurrentlyAsync(
                connection,
                GetReceivedTableName(),
                indexName: "idx_received_Content_trgm",
                cancellationToken
            )
            .ConfigureAwait(false);

        await _EnsureContentTrgmIndexConcurrentlyAsync(
                connection,
                GetPublishedTableName(),
                indexName: "idx_published_Content_trgm",
                cancellationToken
            )
            .ConfigureAwait(false);

        await _EnsureOwnerIndexConcurrentlyAsync(
                connection,
                GetReceivedTableName(),
                indexName: "idx_received_Owner_not_null",
                cancellationToken
            )
            .ConfigureAwait(false);

        await _EnsureOwnerIndexConcurrentlyAsync(
                connection,
                GetPublishedTableName(),
                indexName: "idx_published_Owner_not_null",
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
        // SIGTERM mid-build leaves the index in `indisvalid=false` state. `CREATE INDEX CONCURRENTLY
        // IF NOT EXISTS` matches only by name and silently skips an invalid index, so the retry-pickup
        // path falls back to a seq scan and the broker queue backs up. Probe pg_index first and drop
        // an invalid index before re-creating.
        await _DropInvalidIndexConcurrentlyAsync(connection, indexName, cancellationToken).ConfigureAwait(false);

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

    private async Task _EnsureContentTrgmIndexConcurrentlyAsync(
        NpgsqlConnection connection,
        string qualifiedTable,
        string indexName,
        CancellationToken cancellationToken
    )
    {
        // pg_trgm GIN index accelerates ILIKE / similarity searches on the Content column used by
        // the dashboard message-list filter. CONCURRENTLY avoids an AccessExclusiveLock on hot tables.
        // SIGTERM mid-build leaves the index in `indisvalid=false`; repair it on the next boot.
        await _DropInvalidIndexConcurrentlyAsync(connection, indexName, cancellationToken).ConfigureAwait(false);

        var createIndex = $"""
            CREATE INDEX CONCURRENTLY IF NOT EXISTS "{indexName}" ON {qualifiedTable} USING gin ("Content" gin_trgm_ops);
            """;

        await connection
            .ExecuteNonQueryAsync(
                createIndex,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task _EnsureOwnerIndexConcurrentlyAsync(
        NpgsqlConnection connection,
        string qualifiedTable,
        string indexName,
        CancellationToken cancellationToken
    )
    {
        await _DropInvalidIndexConcurrentlyAsync(connection, indexName, cancellationToken).ConfigureAwait(false);

        var createIndex = $"""
            CREATE INDEX CONCURRENTLY IF NOT EXISTS "{indexName}" ON {qualifiedTable} ("Owner") WHERE "Owner" IS NOT NULL;
            """;

        await connection
            .ExecuteNonQueryAsync(
                createIndex,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Probes <c>pg_index</c> for an existing index with the given name. If found AND
    /// <c>indisvalid=false</c> (typical of a SIGTERM'd <c>CREATE INDEX CONCURRENTLY</c> build),
    /// issues <c>DROP INDEX CONCURRENTLY IF EXISTS</c> so the subsequent re-create starts clean.
    /// If found AND valid, does nothing. If not found, does nothing.
    /// </summary>
    private async Task _DropInvalidIndexConcurrentlyAsync(
        NpgsqlConnection connection,
        string indexName,
        CancellationToken cancellationToken
    )
    {
        const string probeSql = """
            SELECT i.indisvalid
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_index i ON i.indexrelid = c.oid
            WHERE c.relname = @IndexName AND n.nspname = @Schema
            LIMIT 1;
            """;

        await using var probeCommand = new NpgsqlCommand(probeSql, connection)
        {
            CommandTimeout = (int)
                Math.Min(Math.Ceiling(messagingOptions.Value.CommandTimeout.TotalSeconds), int.MaxValue),
        };
        probeCommand.Parameters.Add(new NpgsqlParameter("@IndexName", indexName));
        probeCommand.Parameters.Add(new NpgsqlParameter("@Schema", postgreSqlOptions.Value.Schema));

        var probeResult = await probeCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        if (probeResult is bool isValid && !isValid)
        {
            // The leftover index would otherwise be matched by `CREATE INDEX ... IF NOT EXISTS` and
            // skipped, leaving the seq-scan fallback in place. Drop it concurrently so writes stay
            // live during the repair.
            var dropSql = $"""DROP INDEX CONCURRENTLY IF EXISTS "{postgreSqlOptions.Value.Schema}"."{indexName}";""";

            await connection
                .ExecuteNonQueryAsync(
                    dropSql,
                    commandTimeout: messagingOptions.Value.CommandTimeout,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            logger.LogInvalidIndexDropped(indexName, postgreSqlOptions.Value.Schema);
        }
    }

    private string _CreateDbTablesScript(string schema)
    {
        var batchSql = $"""
            -- pg_trgm is required for GIN trigram indexes on the Content column (dashboard search).
            -- CREATE EXTENSION is idempotent and safe inside a transaction on PostgreSQL 9.1+.
            CREATE EXTENSION IF NOT EXISTS pg_trgm;

            CREATE SCHEMA IF NOT EXISTS "{schema}";

            CREATE TABLE IF NOT EXISTS {GetReceivedTableName()}(
                "Id" UUID PRIMARY KEY NOT NULL,
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
                "Owner" VARCHAR({postgreSqlOptions.Value.OwnerColumnMaxLength}) NULL,
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
            -- #8 — standalone "StatusName" index so GetStatisticsAsync / per-status COUNTs do an index
            -- scan instead of a full sequential scan on large tables (existing composite indexes lead
            -- with ExpiresAt/Version, so they do not serve a bare "StatusName" = '...' predicate).
            CREATE INDEX IF NOT EXISTS "idx_received_StatusName" ON {GetReceivedTableName()} ("StatusName");

            CREATE TABLE IF NOT EXISTS {GetPublishedTableName()}(
                "Id" UUID PRIMARY KEY NOT NULL,
                "Version" VARCHAR(20) NOT NULL,
            	"Name" VARCHAR(200) NOT NULL,
            	"Content" TEXT NULL,
                "IntentType" SMALLINT NOT NULL,
            	"Retries" INT NOT NULL,
            	"Added" TIMESTAMPTZ NOT NULL,
                "ExpiresAt" TIMESTAMPTZ NULL,
                "NextRetryAt" TIMESTAMPTZ NULL,
                "LockedUntil" TIMESTAMPTZ NULL,
                "Owner" VARCHAR({postgreSqlOptions.Value.OwnerColumnMaxLength}) NULL,
            	"StatusName" VARCHAR(50) NOT NULL,
                "MessageId" VARCHAR(200) NOT NULL
            );

            CREATE INDEX IF NOT EXISTS "idx_published_ExpiresAt_StatusName" ON {GetPublishedTableName()}("ExpiresAt","StatusName");
            CREATE INDEX IF NOT EXISTS "idx_published_Version_ExpiresAt_StatusName" ON {GetPublishedTableName()} ("Version","ExpiresAt","StatusName");
            -- #8 — see the matching comment on the received-table block above; the partial
            -- retry-pickup index for published is also created post-transaction via
            -- _EnsureRetryPickupIndexConcurrentlyAsync.
            CREATE INDEX IF NOT EXISTS "idx_published_delayed" ON {GetPublishedTableName()} ("StatusName","ExpiresAt") WHERE "StatusName" = 'Delayed';
            -- #8 — see the received-table note above; standalone "StatusName" index for the
            -- dashboard statistics COUNTs.
            CREATE INDEX IF NOT EXISTS "idx_published_StatusName" ON {GetPublishedTableName()} ("StatusName");

            """;

        return batchSql;
    }
}
