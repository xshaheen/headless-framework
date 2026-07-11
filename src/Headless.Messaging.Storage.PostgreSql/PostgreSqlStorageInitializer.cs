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
    // Timeout budget for schema-init DDL — the CONCURRENTLY index builds/drops, the CREATE EXTENSION
    // probe, and the advisory-lock waits that gate them. Decoupled from the OLTP CommandTimeout because
    // these can run for minutes-to-hours on a large table. null (default) => TimeSpan.Zero => Npgsql
    // CommandTimeout 0 => no timeout (wait indefinitely). See PostgreSqlOptions.DdlCommandTimeout (#510).
    private TimeSpan _GetDdlCommandTimeout() => postgreSqlOptions.Value.DdlCommandTimeout ?? TimeSpan.Zero;

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
    /// <para>
    /// The optional <c>pg_trgm</c> extension (which powers the dashboard content trigram search) is
    /// ensured on a best-effort basis <b>outside</b> the transaction: on managed PostgreSQL
    /// (AWS RDS, Azure, Neon, Supabase) the application role typically lacks <c>CREATE EXTENSION</c>,
    /// and a failure there must not roll back the whole schema batch. When <c>pg_trgm</c> is absent the
    /// trigram content indexes are skipped and dashboard content search is unavailable, but all
    /// write/retry paths initialize normally. A DBA can pre-install <c>pg_trgm</c> to enable it.
    /// </para>
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

        // #507 — ensure pg_trgm BEFORE (and outside) the transactional batch. CREATE EXTENSION needs
        // superuser / an elevated role that managed PostgreSQL withholds; running it as the first
        // statement of the transaction meant a permission error rolled back the entire schema batch and
        // left messaging dead at startup. The extension only powers the dashboard trigram (ILIKE) content
        // search — never a write or retry-pickup path — so degrade gracefully when it is unavailable.
        var trgmAvailable = await _TryEnsureTrgmExtensionAsync(connection, cancellationToken).ConfigureAwait(false);

        // #6 — serialize concurrent-replica boots on a stable advisory-lock key derived from the schema
        // name (hashtextextended is deterministic across sessions and needs no superuser, unlike
        // CREATE EXTENSION). Without it two replicas booting together can race the CONCURRENTLY builds /
        // probe-then-DROP below and one replica's startup fails (InitializeAsync has no retry).
        var schema = postgreSqlOptions.Value.Schema;
        object[] lockParams = [new NpgsqlParameter("@Schema", schema)];

        // PostgreSQL supports transactional DDL — wrap the batch so a mid-script failure
        // (network drop, broker-side abort) cannot leave the schema half-initialized. The
        // transaction-scoped advisory lock is released automatically on COMMIT/ROLLBACK.
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
        {
            // #510 — the lock WAIT uses the DDL timeout, not the OLTP one: a peer replica can hold this
            // same key (session-level) across a multi-minute CONCURRENTLY build below, so a 30s wait here
            // would fail this replica's startup while the peer legitimately builds.
            await connection
                .ExecuteNonQueryAsync(
                    "SELECT pg_advisory_xact_lock(hashtextextended(@Schema, 0));",
                    transaction: transaction,
                    commandTimeout: _GetDdlCommandTimeout(),
                    sqlParams: lockParams,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            await connection
                .ExecuteNonQueryAsync(
                    sql,
                    transaction: transaction,
                    commandTimeout: messagingOptions.Value.CommandTimeout,
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        // Retry-pickup partial indexes and trigram content indexes use CREATE INDEX CONCURRENTLY so
        // the AccessExclusiveLock is replaced with a ShareUpdateExclusiveLock — readers and writers
        // stay live during the create. CONCURRENTLY cannot run inside a transaction (PG raises 25001),
        // so these run on an autocommit connection AFTER the schema/table DDL has committed above.
        // A session-level advisory lock (same key) serializes this phase across replicas so two booters
        // don't race the same CONCURRENTLY build / probe-then-DROP.
        // #510 — DDL timeout for the same reason as the xact lock above: this session-level lock is held
        // for the full duration of the CONCURRENTLY phase, so a peer's wait must not expire at the OLTP budget.
        await connection
            .ExecuteNonQueryAsync(
                "SELECT pg_advisory_lock(hashtextextended(@Schema, 0));",
                commandTimeout: _GetDdlCommandTimeout(),
                sqlParams: lockParams,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        try
        {
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

            // #507 — the trigram content indexes require pg_trgm (gin_trgm_ops). Skip them (and their
            // probe-then-DROP repair) when the extension is unavailable so the rest of schema init
            // completes; dashboard content search stays off until a DBA installs pg_trgm.
            if (trgmAvailable)
            {
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
            }
            else
            {
                logger.LogTrgmContentIndexSkipped();
            }

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
        }
        finally
        {
            // Release the session advisory lock even if a CONCURRENTLY build was cancelled. Closing the
            // connection would also release it, but unlock explicitly so a pooled connection comes back clean.
            await connection
                .ExecuteNonQueryAsync(
                    "SELECT pg_advisory_unlock(hashtextextended(@Schema, 0));",
                    commandTimeout: messagingOptions.Value.CommandTimeout,
                    sqlParams: [new NpgsqlParameter("@Schema", schema)],
                    cancellationToken: CancellationToken.None
                )
                .ConfigureAwait(false);
        }

        logger.LogEnsuringTablesCreated();
    }

    /// <summary>
    /// Best-effort <c>CREATE EXTENSION IF NOT EXISTS pg_trgm</c> on the autocommit connection — never
    /// inside the schema transaction, so a permission failure cannot roll the batch back — followed by an
    /// authoritative probe of <c>pg_extension</c>. Returns whether <c>pg_trgm</c> is installed: it may
    /// already be present (pre-installed by a DBA) even when this role lacks <c>CREATE EXTENSION</c>.
    /// </summary>
    private async Task<bool> _TryEnsureTrgmExtensionAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await connection
                .ExecuteNonQueryAsync(
                    "CREATE EXTENSION IF NOT EXISTS pg_trgm;",
                    commandTimeout: _GetDdlCommandTimeout(),
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (PostgresException ex)
        {
            // Managed PostgreSQL (RDS/Azure/Neon/Supabase) restricts CREATE EXTENSION to superusers, and a
            // self-hosted server may not ship the pg_trgm contrib package at all. Either way the extension
            // is optional (dashboard content search only) — log and fall through to the probe, which reports
            // the real state. On an autocommit connection this failed statement does not poison the session,
            // so the probe below still runs (the whole point of doing this outside the transaction).
            logger.LogTrgmExtensionUnavailable(ex.SqlState, ex.MessageText);
        }

        var installed = await connection
            .ExecuteScalarAsync(
                "SELECT COUNT(1) FROM pg_extension WHERE extname = 'pg_trgm';",
                commandTimeout: messagingOptions.Value.CommandTimeout,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        return installed > 0;
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
                commandTimeout: _GetDdlCommandTimeout(),
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
                commandTimeout: _GetDdlCommandTimeout(),
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
                commandTimeout: _GetDdlCommandTimeout(),
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

        await using var probeCommand = new NpgsqlCommand(probeSql, connection);
        probeCommand.CommandTimeout = (int)
            Math.Min(Math.Ceiling(messagingOptions.Value.CommandTimeout.TotalSeconds), int.MaxValue);
        probeCommand.Parameters.Add(new NpgsqlParameter("@IndexName", indexName));
        probeCommand.Parameters.Add(new NpgsqlParameter("@Schema", postgreSqlOptions.Value.Schema));

        var probeResult = await probeCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        if (probeResult is bool isValid && !isValid)
        {
            // The leftover index would otherwise be matched by `CREATE INDEX ... IF NOT EXISTS` and
            // skipped, leaving the seq-scan fallback in place. Drop it concurrently so writes stay
            // live during the repair.
            var dropSql = $"""DROP INDEX CONCURRENTLY IF EXISTS "{postgreSqlOptions.Value.Schema}"."{indexName}";""";

            // #510 — the repair DROP is itself a CONCURRENTLY op that can run long on a busy table, so it
            // uses the DDL timeout. The probe SELECT above stays on the OLTP budget: it is a fast catalog
            // lookup and giving it an unbounded timeout would risk hanging startup on a locked catalog.
            await connection
                .ExecuteNonQueryAsync(
                    dropSql,
                    commandTimeout: _GetDdlCommandTimeout(),
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            logger.LogInvalidIndexDropped(indexName, postgreSqlOptions.Value.Schema);
        }
    }

    private string _CreateDbTablesScript(string schema)
    {
        var batchSql = string.Create(
            CultureInfo.InvariantCulture,
            $"""
            -- #507 — pg_trgm (required by the Content GIN trigram indexes for dashboard search) is NOT
            -- created here. CREATE EXTENSION needs a privilege managed PostgreSQL withholds, and a failure
            -- inside this transaction would roll back the whole schema batch. It is instead ensured
            -- best-effort BEFORE this transaction in _TryEnsureTrgmExtensionAsync; the trigram indexes are
            -- skipped when it is absent.
            CREATE SCHEMA IF NOT EXISTS "{schema}";

            CREATE TABLE IF NOT EXISTS {GetReceivedTableName()}(
                "Id" UUID PRIMARY KEY NOT NULL,
                "Version" VARCHAR(20) NOT NULL,
            	"Name" VARCHAR(200) NOT NULL,
            	"Group" VARCHAR(200) NULL,
            	"Content" TEXT NULL,
                "IntentType" SMALLINT NOT NULL,
                "Retries" INT NOT NULL,
                "InlineAttempts" INT NOT NULL DEFAULT 0,
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
            -- #508 — ("StatusName","Added") composite serves BOTH the dashboard hourly-timeline query
            -- (WHERE "StatusName"=$1 AND "Added" BETWEEN … — a StatusName seek + Added range scan) and the
            -- per-status COUNTs in GetStatisticsAsync via its "StatusName" prefix. It strictly subsumes the
            -- earlier standalone "StatusName" index (#8), which is dropped to avoid a redundant, write-
            -- amplifying second index. The existing composite indexes lead with ExpiresAt/Version and so
            -- serve neither predicate.
            DROP INDEX IF EXISTS "{schema}"."idx_received_StatusName";
            CREATE INDEX IF NOT EXISTS "idx_received_StatusName_Added" ON {GetReceivedTableName()} ("StatusName","Added");

            CREATE TABLE IF NOT EXISTS {GetPublishedTableName()}(
                "Id" UUID PRIMARY KEY NOT NULL,
                "Version" VARCHAR(20) NOT NULL,
            	"Name" VARCHAR(200) NOT NULL,
            	"Content" TEXT NULL,
                "IntentType" SMALLINT NOT NULL,
                "Retries" INT NOT NULL,
                "InlineAttempts" INT NOT NULL DEFAULT 0,
            	"Added" TIMESTAMPTZ NOT NULL,
                "ExpiresAt" TIMESTAMPTZ NULL,
                "NextRetryAt" TIMESTAMPTZ NULL,
                "LockedUntil" TIMESTAMPTZ NULL,
                "Owner" VARCHAR({postgreSqlOptions.Value.OwnerColumnMaxLength}) NULL,
            	"StatusName" VARCHAR(50) NOT NULL,
                "MessageId" VARCHAR(200) NOT NULL
            );

            ALTER TABLE {GetReceivedTableName()} ADD COLUMN IF NOT EXISTS "InlineAttempts" INT NOT NULL DEFAULT 0;
            ALTER TABLE {GetPublishedTableName()} ADD COLUMN IF NOT EXISTS "InlineAttempts" INT NOT NULL DEFAULT 0;

            CREATE INDEX IF NOT EXISTS "idx_published_ExpiresAt_StatusName" ON {GetPublishedTableName()}("ExpiresAt","StatusName");
            CREATE INDEX IF NOT EXISTS "idx_published_Version_ExpiresAt_StatusName" ON {GetPublishedTableName()} ("Version","ExpiresAt","StatusName");
            -- #8 — see the matching comment on the received-table block above; the partial
            -- retry-pickup index for published is also created post-transaction via
            -- _EnsureRetryPickupIndexConcurrentlyAsync.
            CREATE INDEX IF NOT EXISTS "idx_published_delayed" ON {GetPublishedTableName()} ("StatusName","ExpiresAt") WHERE "StatusName" = 'Delayed';
            -- #509 — partial index for the Queued branch of ScheduleMessagesOfDelayedAsync's OR predicate
            -- (WHERE "Version"=$1 AND ("ExpiresAt"<$2 AND "StatusName"='Queued')). Leading with
            -- ("Version","ExpiresAt") gives a version seek + ExpiresAt range scan, which the planner can
            -- bitmap-OR with the Delayed partial index above instead of sequentially scanning a large
            -- Queued backlog (e.g. accumulated during broker downtime).
            CREATE INDEX IF NOT EXISTS "idx_published_Version_ExpiresAt_Queued" ON {GetPublishedTableName()} ("Version","ExpiresAt") WHERE "StatusName" = 'Queued';
            -- #508 — see the received-table note above; ("StatusName","Added") composite replaces the
            -- standalone "StatusName" index for the dashboard hourly-timeline query and statistics COUNTs.
            DROP INDEX IF EXISTS "{schema}"."idx_published_StatusName";
            CREATE INDEX IF NOT EXISTS "idx_published_StatusName_Added" ON {GetPublishedTableName()} ("StatusName","Added");

            """
        );

        return batchSql;
    }
}
