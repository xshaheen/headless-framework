// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Constants;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Sql.PostgreSql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Messaging.Storage.PostgreSql;

/// <summary>
/// PostgreSQL implementation of <see cref="IStorageInitializer"/> for database schema setup.
/// Creates required tables (messaging_published, messaging_received) and indexes on first run.
/// </summary>
internal sealed class PostgreSqlStorageInitializer(
    ILogger<PostgreSqlStorageInitializer> logger,
    IOptions<PostgreSqlOptions> postgreSqlOptions,
    IOptions<MessagingStorageOptions> storageOptions,
    IOptions<MessagingOptions> messagingOptions
) : IStorageInitializer
{
    /// <summary>
    /// Creates only the published table and its indexes. Set for an additional outbox, whose database holds
    /// published rows only: the inbox, its history, and its readiness state stay in the primary storage's database.
    /// </summary>
    internal bool OutboxOnly { get; set; }

    // Timeout budget for schema-init DDL — the CONCURRENTLY index builds/drops, the CREATE EXTENSION
    // probe, and the advisory-lock waits that gate them. Decoupled from the OLTP CommandTimeout because
    // these can run for minutes-to-hours on a large table. null (default) => TimeSpan.Zero => Npgsql
    // CommandTimeout 0 => no timeout (wait indefinitely). See PostgreSqlOptions.DdlCommandTimeout (#510).
    private TimeSpan _GetDdlCommandTimeout()
    {
        return postgreSqlOptions.Value.DdlCommandTimeout ?? TimeSpan.Zero;
    }

    private static readonly TimeSpan _InitLockPollInterval = TimeSpan.FromMilliseconds(100);

    // A replica can hold the init lock across a multi-minute CONCURRENTLY build, and with the default unbounded DDL
    // timeout a waiter would otherwise sit silent. Report the wait once it outlasts a normal boot, then periodically,
    // so a stuck startup shows which lock it is waiting on instead of looking hung.
    private static readonly TimeSpan _InitLockFirstWaitLog = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _InitLockWaitLogInterval = TimeSpan.FromSeconds(30);

    // Polls pg_try_advisory_lock instead of blocking in pg_advisory_lock: a blocked statement keeps a
    // snapshot open, and the holder's CREATE INDEX CONCURRENTLY waits for that snapshot, so a blocking
    // wait deadlocks two replicas booting together on a fresh schema. Between attempts this session
    // holds no snapshot, which lets the holder's index builds finish.
    // #510 — the wait is bounded by the DDL timeout, not the OLTP one, because the holder can keep the
    // lock across a multi-minute CONCURRENTLY build.
    private async Task _AcquireInitLockAsync(
        NpgsqlConnection connection,
        string lockResource,
        CancellationToken cancellationToken
    )
    {
        var timeout = _GetDdlCommandTimeout();
        var startedAt = Stopwatch.GetTimestamp();
        var nextWaitLog = _InitLockFirstWaitLog;

        while (true)
        {
            var acquired = await connection
                .ExecuteScalarAsync(
                    "SELECT pg_try_advisory_lock(hashtextextended(@LockResource, 0))::int;",
                    commandTimeout: messagingOptions.Value.CommandTimeout,
                    sqlParams: [new NpgsqlParameter("@LockResource", lockResource)],
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            if (acquired == 1)
            {
                return;
            }

            var elapsed = Stopwatch.GetElapsedTime(startedAt);

            if (timeout > TimeSpan.Zero && elapsed >= timeout)
            {
                throw new TimeoutException(
                    $"Timed out after {timeout} waiting for the Headless.Messaging initialization lock '{lockResource}'."
                );
            }

            if (elapsed >= nextWaitLog)
            {
                logger.LogWaitingForInitLock(lockResource, elapsed);
                nextWaitLog = elapsed + _InitLockWaitLogInterval;
            }

            await Task.Delay(_InitLockPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns the fully-qualified PostgreSQL table name for published outbox messages,
    /// in the form <c>"schema"."messaging_published"</c>.
    /// </summary>
    public string GetPublishedTableName()
    {
        return $"\"{storageOptions.Value.Schema}\".\"messaging_published\"";
    }

    /// <summary>
    /// Returns the fully-qualified PostgreSQL table name for received outbox messages,
    /// in the form <c>"schema"."messaging_received"</c>.
    /// </summary>
    public string GetReceivedTableName()
    {
        return $"\"{storageOptions.Value.Schema}\".\"messaging_received\"";
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

        var sql = _CreateDbTablesScript(storageOptions.Value.Schema);
        await using var connection = postgreSqlOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // #507 — ensure pg_trgm BEFORE (and outside) the transactional batch. CREATE EXTENSION needs
        // superuser / an elevated role that managed PostgreSQL withholds; running it as the first
        // statement of the transaction meant a permission error rolled back the entire schema batch and
        // left messaging dead at startup. The extension only powers the dashboard trigram (ILIKE) content
        // search — never a write or retry-pickup path — so degrade gracefully when it is unavailable.
        var trgmAvailable = await _TryEnsureTrgmExtensionAsync(connection, cancellationToken).ConfigureAwait(false);

        // #6 — serialize concurrent-replica boots on one session-level advisory lock held across the
        // transactional DDL and the CONCURRENTLY phase. Without it two replicas booting together can race
        // the CONCURRENTLY builds / probe-then-DROP below and one replica's startup fails (InitializeAsync
        // has no retry). The key is Messaging-namespaced so another feature sharing the schema never
        // contends on it; hashtextextended is deterministic across sessions and needs no superuser.
        var schema = storageOptions.Value.Schema;
        var lockResource = $"headless_messaging_init:{schema}";
        await _AcquireInitLockAsync(connection, lockResource, cancellationToken).ConfigureAwait(false);

        try
        {
            await _RunTransactionalDdlAsync(connection, sql, cancellationToken).ConfigureAwait(false);

            // Retry-pickup partial indexes and trigram content indexes use CREATE INDEX CONCURRENTLY so
            // the AccessExclusiveLock is replaced with a ShareUpdateExclusiveLock — readers and writers
            // stay live during the create. CONCURRENTLY cannot run inside a transaction (PG raises 25001),
            // so these run on an autocommit connection AFTER the schema/table DDL has committed above.
            if (!OutboxOnly)
            {
                await _EnsureRetryPickupIndexConcurrentlyAsync(
                        connection,
                        GetReceivedTableName(),
                        indexName: "idx_messaging_received_version_next_retry_at",
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            await _EnsureRetryPickupIndexConcurrentlyAsync(
                    connection,
                    GetPublishedTableName(),
                    indexName: "idx_messaging_published_version_next_retry_at",
                    cancellationToken
                )
                .ConfigureAwait(false);

            // #507 — the trigram content indexes require pg_trgm (gin_trgm_ops). Skip them (and their
            // probe-then-DROP repair) when the extension is unavailable so the rest of schema init
            // completes; dashboard content search stays off until a DBA installs pg_trgm.
            if (trgmAvailable)
            {
                if (!OutboxOnly)
                {
                    await _EnsureContentTrgmIndexConcurrentlyAsync(
                            connection,
                            GetReceivedTableName(),
                            indexName: "idx_messaging_received_content_trgm",
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                }

                await _EnsureContentTrgmIndexConcurrentlyAsync(
                        connection,
                        GetPublishedTableName(),
                        indexName: "idx_messaging_published_content_trgm",
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            else
            {
                logger.LogTrgmContentIndexSkipped();
            }

            if (!OutboxOnly)
            {
                await _EnsureOwnerIndexConcurrentlyAsync(
                        connection,
                        GetReceivedTableName(),
                        indexName: "idx_messaging_received_owner_not_null",
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            await _EnsureOwnerIndexConcurrentlyAsync(
                    connection,
                    GetPublishedTableName(),
                    indexName: "idx_messaging_published_owner_not_null",
                    cancellationToken
                )
                .ConfigureAwait(false);

            // An additional outbox has no inbox: its history tables and readiness state live in the primary database.
            if (!OutboxOnly)
            {
                // History tables can already contain an unbounded backlog on an existing schema.
                foreach (
                    var (indexName, table, columns) in new[]
                    {
                        (
                            "idx_messaging_inbox_receipts_type_created",
                            "messaging_inbox_operation_receipts",
                            "\"operation_type\",\"created_at\""
                        ),
                        (
                            "idx_messaging_inbox_audit_type_created",
                            "messaging_inbox_audit",
                            "\"operation_type\",\"created_at\""
                        ),
                        ("idx_messaging_inbox_audit_operation", "messaging_inbox_audit", "\"operation_id\""),
                    }
                )
                {
                    await _DropInvalidIndexConcurrentlyAsync(connection, indexName, cancellationToken)
                        .ConfigureAwait(false);
                    await connection
                        .ExecuteNonQueryAsync(
                            $"CREATE INDEX CONCURRENTLY IF NOT EXISTS \"{indexName}\" ON \"{storageOptions.Value.Schema}\".\"{table}\" ({columns});",
                            commandTimeout: _GetDdlCommandTimeout(),
                            cancellationToken: cancellationToken
                        )
                        .ConfigureAwait(false);
                }

                await _PublishInboxSchemaReadinessAsync(connection, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await _ReleaseInitLockAsync(connection, lockResource).ConfigureAwait(false);
        }

        logger.LogEnsuringTablesCreated();
    }

    // PostgreSQL supports transactional DDL, so the batch runs in one transaction and a mid-script failure (network
    // drop, broker-side abort) cannot leave the schema half-initialized. The Messaging and schema-wide advisory locks
    // serialize Headless initializers, but a schema or object creator outside them (a consumer's EF migration, other
    // application code) can still commit the same CREATE first. That fails the batch with 42P06/42P07/42710, or 23505
    // on the catalog unique index when the two inserts race, and the rollback discards the whole batch. The
    // conflicting creator has committed by the time we see the error, so one rerun in a fresh transaction passes the
    // IF NOT EXISTS guards; a second failure is not a race and propagates.
    private async Task _RunTransactionalDdlAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var transaction = await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await connection
                    .ExecuteNonQueryAsync(
                        sql,
                        transaction: transaction,
                        commandTimeout: messagingOptions.Value.CommandTimeout,
                        cancellationToken: cancellationToken
                    )
                    .ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return;
            }
            catch (PostgresException ex)
                when (attempt == 1
                    && ex.SqlState
                        is SqlErrorCodes.PostgreSql.DuplicateSchema
                            or SqlErrorCodes.PostgreSql.DuplicateTable
                            or SqlErrorCodes.PostgreSql.DuplicateObject
                            or SqlErrorCodes.PostgreSql.UniqueViolation
                )
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                logger.LogSchemaRaceObserved(ex.SqlState, ex.MessageText);
            }
        }
    }

    // Release the session advisory lock even if a CONCURRENTLY build was cancelled. Closing the connection would also
    // release it, but unlock explicitly so a pooled connection comes back clean. This runs from a finally block, so
    // an unlock failure (typically the same broken connection that failed the DDL) is logged rather than thrown: a
    // throw here would replace the exception that explains why initialization failed, and a dead session has already
    // released its advisory locks.
    private async Task _ReleaseInitLockAsync(NpgsqlConnection connection, string lockResource)
    {
        try
        {
            await connection
                .ExecuteNonQueryAsync(
                    "SELECT pg_advisory_unlock(hashtextextended(@LockResource, 0));",
                    commandTimeout: messagingOptions.Value.CommandTimeout,
                    sqlParams: [new NpgsqlParameter("@LockResource", lockResource)],
                    cancellationToken: CancellationToken.None
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogInitLockReleaseFailed(lockResource, ex);
        }
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
        // A failed concurrent build can leave an unusable index under the intended name.
        await _DropInvalidIndexConcurrentlyAsync(connection, indexName, cancellationToken).ConfigureAwait(false);

        var createIndex = $"""
            CREATE INDEX CONCURRENTLY IF NOT EXISTS "{indexName}" ON {qualifiedTable} ("version","intent_type","next_retry_at") INCLUDE ("retries","locked_until") WHERE "next_retry_at" IS NOT NULL;
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
            CREATE INDEX CONCURRENTLY IF NOT EXISTS "{indexName}" ON {qualifiedTable} USING gin ("content" gin_trgm_ops);
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
            CREATE INDEX CONCURRENTLY IF NOT EXISTS "{indexName}" ON {qualifiedTable} ("owner") WHERE "owner" IS NOT NULL;
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
        probeCommand.Parameters.Add(new NpgsqlParameter("@Schema", storageOptions.Value.Schema));

        var probeResult = await probeCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        if (probeResult is bool isValid && !isValid)
        {
            // The leftover index would otherwise be matched by `CREATE INDEX ... IF NOT EXISTS` and
            // skipped, leaving the seq-scan fallback in place. Drop it concurrently so writes stay
            // live during the repair.
            var dropSql = $"""DROP INDEX CONCURRENTLY IF EXISTS "{storageOptions.Value.Schema}"."{indexName}";""";

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

            logger.LogInvalidIndexDropped(indexName, storageOptions.Value.Schema);
        }
    }

    private async Task _PublishInboxSchemaReadinessAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        var finalIndexCount = await connection
            .ExecuteScalarAsync(
                "SELECT COUNT(1) FROM pg_indexes WHERE schemaname=@Schema AND indexname IN ('uq_messaging_received_inbox_root_key','uq_messaging_received_inbox_lifecycle_generation');",
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: [new NpgsqlParameter("@Schema", storageOptions.Value.Schema)],
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        if (finalIndexCount != 2)
        {
            throw new InvalidOperationException(
                "Headless.Messaging inbox schema is incomplete: the final inbox key index is missing."
            );
        }

        var operationShapeReady = await connection
            .ExecuteScalarAsync(
                """
                SELECT (
                    EXISTS (
                        SELECT 1 FROM pg_constraint
                        WHERE conname='ck_messaging_received_inbox_identity'
                          AND conrelid=format('%I.messaging_received', @Schema)::regclass
                    )
                    AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_messaging_received_inbox_lifecycle'
                        AND conrelid = format('%I.messaging_received', @Schema)::regclass)
                    AND EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_schema=@Schema AND table_name='messaging_received' AND column_name='lifecycle_id')
                    AND EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema=@Schema AND table_name='messaging_inbox_operation_receipts' AND column_name='expected_status'
                    )
                    AND EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema=@Schema AND table_name='messaging_inbox_operation_receipts' AND column_name='outcome'
                    )
                    AND EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema=@Schema AND table_name='messaging_inbox_operation_receipts' AND column_name='target_kind'
                    )
                    AND EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema=@Schema AND table_name='messaging_inbox_operation_receipts' AND column_name='expected_due_at'
                    )
                    AND EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema=@Schema AND table_name='messaging_inbox_audit' AND column_name='target_kind'
                    )
                )::int;
                """,
                commandTimeout: messagingOptions.Value.CommandTimeout,
                sqlParams: [new NpgsqlParameter("@Schema", storageOptions.Value.Schema)],
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        if (operationShapeReady != 1)
        {
            throw new InvalidOperationException(
                "Headless.Messaging inbox schema is incomplete: the lifecycle, retention or operation receipt contract is missing."
            );
        }

        var sql = $"""
            INSERT INTO "{storageOptions.Value.Schema}"."messaging_schema_state" ("component","schema_version","ready_at")
            VALUES ('inbox', 1, statement_timestamp())
            ON CONFLICT ("component") DO UPDATE
            SET "schema_version"=EXCLUDED."schema_version", "ready_at"=EXCLUDED."ready_at";
            """;
        await connection
            .ExecuteNonQueryAsync(sql, commandTimeout: _GetDdlCommandTimeout(), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private string _CreateDbTablesScript(string schema)
    {
        // An additional outbox gets the schema and its published table only; see OutboxOnly.
        return OutboxOnly
            ? _CreateSchemaScript(schema) + _CreatePublishedTableScript()
            : _CreateSchemaScript(schema) + _CreateInboxTablesScript(schema) + _CreatePublishedTableScript();
    }

    // The schema init lock and the messaging schema itself, shared by the inbox and published tables.
    private static string _CreateSchemaScript(string schema)
    {
        return $"""
            -- #507 — pg_trgm (required by the Content GIN trigram indexes for dashboard search) is NOT
            -- created here. CREATE EXTENSION needs a privilege managed PostgreSQL withholds, and a failure
            -- inside this transaction would roll back the whole schema batch. It is instead ensured
            -- best-effort BEFORE this transaction in _TryEnsureTrgmExtensionAsync; the trigram indexes are
            -- skipped when it is absent.
            {PostgreSqlSchemaInitLock.AcquireStatement(schema)}
            CREATE SCHEMA IF NOT EXISTS "{schema}";

            """;
    }

    // The received table, the inbox history tables, and the schema readiness state.
    private string _CreateInboxTablesScript(string schema)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"""
            DO $inbox_schema_guard$
            DECLARE current_schema_version integer;
            BEGIN
                IF to_regclass('"{schema}"."messaging_schema_state"') IS NOT NULL THEN
                    SELECT "schema_version" INTO current_schema_version
                    FROM "{schema}"."messaging_schema_state"
                    WHERE "component"='inbox';

                    IF current_schema_version > 1 THEN
                        RAISE EXCEPTION 'Headless.Messaging inbox schema version % is newer than supported version 1. Upgrade the application before starting this binary.', current_schema_version;
                    END IF;
                END IF;
            END
            $inbox_schema_guard$;

            CREATE TABLE IF NOT EXISTS {GetReceivedTableName()}(
                "id" UUID PRIMARY KEY NOT NULL,
                "version" VARCHAR(20) NOT NULL,
            	"name" VARCHAR(200) NOT NULL,
            	"content" TEXT NULL,
                "intent_type" SMALLINT NOT NULL,
                "retries" INT NOT NULL,
                "inline_attempts" INT NOT NULL DEFAULT 0,
            	"added" TIMESTAMPTZ NOT NULL,
                "expires_at" TIMESTAMPTZ NULL,
                "next_retry_at" TIMESTAMPTZ NULL,
                "locked_until" TIMESTAMPTZ NULL,
                "owner" VARCHAR({postgreSqlOptions.Value.OwnerColumnMaxLength}) NULL,
            	"status_name" VARCHAR(50) NOT NULL,
                "message_id" VARCHAR(200) COLLATE "C" NOT NULL,
                "exception_info" text NULL,
                "is_inbox_record" BOOLEAN NOT NULL DEFAULT FALSE,
                "tenant_present" BOOLEAN NOT NULL DEFAULT FALSE,
                "tenant_id" VARCHAR(200) COLLATE "C" NOT NULL DEFAULT '',
                "contract_identity" VARCHAR(200) COLLATE "C" NOT NULL DEFAULT '',
                "contract_version" VARCHAR(100) COLLATE "C" NOT NULL DEFAULT '',
                "consumer_identity" VARCHAR(200) COLLATE "C" NOT NULL DEFAULT '',
                "generation" BIGINT NOT NULL DEFAULT 0,
                "generation_incarnation_id" UUID NULL,
                "lifecycle_id" UUID NULL,
                "attempt_id" UUID NULL,
                "is_inbox_orphaned" BOOLEAN NOT NULL DEFAULT FALSE,
                "is_current_generation" BOOLEAN NOT NULL DEFAULT TRUE,
                "replay_parent_incarnation_id" UUID NULL,
                "replay_operation_id" UUID NULL,
                "terminal_at" TIMESTAMPTZ NULL,
                "effective_expires_at" TIMESTAMPTZ NULL,
                "is_held" BOOLEAN NOT NULL DEFAULT FALSE,
                "held_at" TIMESTAMPTZ NULL,
                "held_by" VARCHAR(200) COLLATE "C" NULL,
                "hold_reason" VARCHAR(1000) NULL,
                "hold_operation_id" UUID NULL,
                "inbox_retention_seconds" BIGINT NOT NULL DEFAULT 2592000
            );

            DO $headless$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint WHERE conname = 'ck_messaging_received_inbox_identity'
                    AND conrelid = '{GetReceivedTableName()}'::regclass
                ) THEN
                    ALTER TABLE {GetReceivedTableName()} ADD CONSTRAINT "ck_messaging_received_inbox_identity" CHECK (
                        NOT "is_inbox_record" OR (
                            "generation" >= 0
                            AND "inbox_retention_seconds" BETWEEN 1 AND 2147483647
                            AND "generation_incarnation_id" IS NOT NULL
                            AND length("message_id") BETWEEN 1 AND 200
                            AND length("contract_identity") BETWEEN 1 AND 200
                            AND length("contract_version") BETWEEN 1 AND 100
                            AND length("consumer_identity") BETWEEN 1 AND 200
                            AND ((NOT "tenant_present" AND "tenant_id" = '') OR ("tenant_present" AND length("tenant_id") BETWEEN 1 AND 200))
                        )
                    );
                END IF;
            END
            $headless$;

            DO $headless$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_messaging_received_inbox_lifecycle'
                    AND conrelid = '{GetReceivedTableName()}'::regclass) THEN
                    ALTER TABLE {GetReceivedTableName()} ADD CONSTRAINT "ck_messaging_received_inbox_lifecycle" CHECK (
                        NOT "is_inbox_record" OR ("lifecycle_id" IS NOT NULL
                            AND ("replay_parent_incarnation_id" IS NOT NULL OR "lifecycle_id" = "generation_incarnation_id"))
                    );
                END IF;
            END
            $headless$;

            CREATE UNIQUE INDEX IF NOT EXISTS "uq_messaging_received_inbox_root_key" ON {GetReceivedTableName()}
                ("tenant_present","tenant_id","message_id","intent_type","contract_identity","contract_version","consumer_identity","generation")
                WHERE "is_inbox_record" AND "replay_parent_incarnation_id" IS NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS "uq_messaging_received_inbox_lifecycle_generation" ON {GetReceivedTableName()}
                ("lifecycle_id","generation") WHERE "is_inbox_record";
            CREATE UNIQUE INDEX IF NOT EXISTS "uq_messaging_received_generation_incarnation" ON {GetReceivedTableName()} ("generation_incarnation_id")
                WHERE "generation_incarnation_id" IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS "uq_messaging_received_non_inbox_consumer_identity" ON {GetReceivedTableName()}
                ("version","message_id","consumer_identity","intent_type") WHERE NOT "is_inbox_record";
            CREATE INDEX IF NOT EXISTS "idx_messaging_received_inbox_retention" ON {GetReceivedTableName()} ("effective_expires_at","id")
                INCLUDE ("status_name","next_retry_at","intent_type") WHERE "is_inbox_record" AND NOT "is_held";
            CREATE INDEX IF NOT EXISTS "idx_messaging_received_expires_at_status_name" ON {GetReceivedTableName()} ("expires_at","status_name");
            CREATE INDEX IF NOT EXISTS "idx_messaging_received_version_expires_at_status_name" ON {GetReceivedTableName()} ("version","expires_at","status_name");
            -- #8 — The partial retry-pickup index (idx_messaging_received_version_next_retry_at) is created
            -- post-transaction with CREATE INDEX CONCURRENTLY in _EnsureRetryPickupIndexConcurrentlyAsync.
            -- CREATE INDEX CONCURRENTLY cannot run inside a transaction; doing the create here would
            -- take an AccessExclusiveLock and block all writers to the hot retry-pickup path during
            -- every replica boot.
            CREATE INDEX IF NOT EXISTS "idx_messaging_received_delayed" ON {GetReceivedTableName()} ("status_name","expires_at") WHERE "status_name" = 'Delayed';
            -- #508 — ("status_name","added") serves BOTH the dashboard hourly-timeline query
            -- (WHERE "status_name"=$1 AND "added" BETWEEN … — a status_name seek + added range scan) and the
            -- per-status COUNTs in GetStatisticsAsync via its "status_name" prefix. The initializer creates
            -- the final schema directly; it does not carry migration DDL for superseded index shapes.
            CREATE INDEX IF NOT EXISTS "idx_messaging_received_status_name_added" ON {GetReceivedTableName()} ("status_name","added");

            CREATE TABLE IF NOT EXISTS "{schema}"."messaging_inbox_operation_receipts"(
                "operation_id" UUID PRIMARY KEY NOT NULL,
                "target_kind" VARCHAR(50) COLLATE "C" NOT NULL DEFAULT 'Inbox',
                "generation_incarnation_id" UUID NULL,
                "operation_type" VARCHAR(50) COLLATE "C" NOT NULL,
                "expected_status" VARCHAR(50) COLLATE "C" NULL,
                "expected_due_at" TIMESTAMPTZ NULL,
                "actor" VARCHAR(200) COLLATE "C" NOT NULL,
                "reason" VARCHAR(1000) NOT NULL,
                "outcome" VARCHAR(50) COLLATE "C" NOT NULL,
                "storage_id" UUID NULL,
                "message_name" VARCHAR(200) NULL,
                "message_id" VARCHAR(200) COLLATE "C" NULL,
                "lane" VARCHAR(50) COLLATE "C" NULL,
                "child_storage_id" UUID NULL,
                "child_generation" BIGINT NULL,
                "child_incarnation_id" UUID NULL,
                "created_at" TIMESTAMPTZ NOT NULL
            );

            CREATE TABLE IF NOT EXISTS "{schema}"."messaging_inbox_audit"(
                "audit_id" UUID PRIMARY KEY NOT NULL,
                "operation_id" UUID NOT NULL,
                "target_kind" VARCHAR(50) COLLATE "C" NOT NULL DEFAULT 'Inbox',
                "generation_incarnation_id" UUID NULL,
                "operation_type" VARCHAR(50) COLLATE "C" NOT NULL,
                "actor" VARCHAR(200) COLLATE "C" NOT NULL,
                "reason" VARCHAR(1000) NOT NULL,
                "outcome" VARCHAR(50) COLLATE "C" NOT NULL,
                "created_at" TIMESTAMPTZ NOT NULL,
                CONSTRAINT "fk_messaging_inbox_audit_operation" FOREIGN KEY ("operation_id")
                    REFERENCES "{schema}"."messaging_inbox_operation_receipts"("operation_id") ON DELETE RESTRICT
            );
            CREATE INDEX IF NOT EXISTS "idx_messaging_inbox_audit_incarnation_created" ON "{schema}"."messaging_inbox_audit" ("generation_incarnation_id","created_at");

            CREATE TABLE IF NOT EXISTS "{schema}"."messaging_schema_state"(
                "component" VARCHAR(50) COLLATE "C" PRIMARY KEY NOT NULL,
                "schema_version" INT NOT NULL,
                "ready_at" TIMESTAMPTZ NOT NULL
            );
            DELETE FROM "{schema}"."messaging_schema_state" WHERE "component"='inbox';

            """
        );
    }

    // The published (outbox) table and its indexes.
    private string _CreatePublishedTableScript()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"""
            CREATE TABLE IF NOT EXISTS {GetPublishedTableName()}(
                "id" UUID PRIMARY KEY NOT NULL,
                "version" VARCHAR(20) NOT NULL,
            	"name" VARCHAR(200) NOT NULL,
            	"content" TEXT NULL,
                "intent_type" SMALLINT NOT NULL,
                "retries" INT NOT NULL,
                "inline_attempts" INT NOT NULL DEFAULT 0,
            	"added" TIMESTAMPTZ NOT NULL,
                "expires_at" TIMESTAMPTZ NULL,
                "next_retry_at" TIMESTAMPTZ NULL,
                "locked_until" TIMESTAMPTZ NULL,
                "owner" VARCHAR({postgreSqlOptions.Value.OwnerColumnMaxLength}) NULL,
            	"status_name" VARCHAR(50) NOT NULL,
                "message_id" VARCHAR(200) NOT NULL
            );

            CREATE INDEX IF NOT EXISTS "idx_messaging_published_expires_at_status_name" ON {GetPublishedTableName()}("expires_at","status_name");
            CREATE INDEX IF NOT EXISTS "idx_messaging_published_version_expires_at_status_name" ON {GetPublishedTableName()} ("version","expires_at","status_name");
            -- #8 — see the matching comment on the received-table block above; the partial
            -- retry-pickup index for published is also created post-transaction via
            -- _EnsureRetryPickupIndexConcurrentlyAsync.
            CREATE INDEX IF NOT EXISTS "idx_messaging_published_delayed" ON {GetPublishedTableName()} ("status_name","expires_at") WHERE "status_name" = 'Delayed';
            -- #509 — partial index for the Queued branch of ScheduleMessagesOfDelayedAsync's OR predicate
            -- (WHERE "version"=$1 AND ("expires_at"<$2 AND "status_name"='Queued')). Leading with
            -- ("version","expires_at") gives a version seek + expires_at range scan, which the planner can
            -- bitmap-OR with the Delayed partial index above instead of sequentially scanning a large
            -- Queued backlog (e.g. accumulated during broker downtime).
            CREATE INDEX IF NOT EXISTS "idx_messaging_published_version_expires_at_queued" ON {GetPublishedTableName()} ("version","expires_at") WHERE "status_name" = 'Queued';
            -- #508 — see the received-table note above; create the final dashboard timeline/statistics index.
            CREATE INDEX IF NOT EXISTS "idx_messaging_published_status_name_added" ON {GetPublishedTableName()} ("status_name","added");

            """
        );
    }
}
