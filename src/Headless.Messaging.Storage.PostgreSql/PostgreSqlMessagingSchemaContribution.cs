// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Headless.Messaging.Configuration;
using Headless.Sql.PostgreSql;

namespace Headless.Messaging.Storage.PostgreSql;

/// <summary>
/// The Messaging feature's schema contribution for PostgreSQL: the published and received message tables, the inbox
/// operation receipt and audit history tables, their indexes, and the optional trigram content indexes, as idempotent
/// steps the Headless schema runner applies.
/// </summary>
internal static class PostgreSqlMessagingSchemaContribution
{
    public const string Feature = "Messaging";
    public const string OutboxFeature = "MessagingOutbox";
    public const string TablesStepVersion = "1";
    public const string PickupIndexesStepVersion = "2";
    public const string ContentSearchStepVersion = "3";

    /// <summary>Creates the contribution of the primary storage: the inbox, its history, and the published table.</summary>
    public static SchemaContribution Create(PostgreSqlOptions options, MessagingStorageOptions storageOptions)
    {
        return _Create(options, storageOptions, inbox: true);
    }

    /// <summary>
    /// Creates the contribution of an additional outbox, whose database holds published rows only: the inbox, its
    /// history, and its readiness checks stay with the primary storage. It is its own feature because its steps differ
    /// from the primary's, and an outbox database never holds both.
    /// </summary>
    public static SchemaContribution CreateOutbox(PostgreSqlOptions options, MessagingStorageOptions storageOptions)
    {
        return _Create(options, storageOptions, inbox: false);
    }

    private static SchemaContribution _Create(
        PostgreSqlOptions options,
        MessagingStorageOptions storageOptions,
        bool inbox
    )
    {
        var schema = storageOptions.Schema;
        var tables = inbox
            ? _InboxTablesSql(schema, options.OwnerColumnMaxLength)
                + _PublishedTableSql(schema, options.OwnerColumnMaxLength)
            : _PublishedTableSql(schema, options.OwnerColumnMaxLength);

        return new SchemaContribution(
            feature: inbox ? Feature : OutboxFeature,
            dialect: PostgreSqlSchemaDialect.Instance,
            createConnection: options.CreateConnection,
            schema: schema,
            steps:
            [
                new SchemaStep(
                    TablesStepVersion,
                    inbox
                        ? "Create the published, received, inbox receipt, and inbox audit tables with their constraints and indexes."
                        : "Create the published table and its indexes.",
                    tables
                ),
                new SchemaStep(
                    PickupIndexesStepVersion,
                    inbox
                        ? "Create the retry-pickup, owner, and history-selection indexes."
                        : "Create the published retry-pickup and owner indexes.",
                    _PickupIndexesSql(schema, inbox)
                ),
                new SchemaStep(
                    ContentSearchStepVersion,
                    "Ensure pg_trgm when permitted and create the dashboard content trigram indexes when it is installed.",
                    _ContentSearchSql(schema, inbox)
                ),
            ]
        );
    }

    private static string _InboxTablesSql(string schema, int ownerColumnMaxLength)
    {
        var received = PostgreSqlStorageTableNames.Received(schema);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"""
            -- #507 — pg_trgm (required by the Content GIN trigram indexes for dashboard search) is NOT
            -- created here. CREATE EXTENSION needs a privilege managed PostgreSQL withholds, and a failure
            -- inside this step would roll back the whole schema batch. The content-search step ensures it
            -- best-effort instead, and skips the trigram indexes when it is absent.
            CREATE TABLE IF NOT EXISTS {received}(
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
                "owner" VARCHAR({ownerColumnMaxLength}) NULL,
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
                    AND conrelid = '{received}'::regclass
                ) THEN
                    ALTER TABLE {received} ADD CONSTRAINT "ck_messaging_received_inbox_identity" CHECK (
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
                    AND conrelid = '{received}'::regclass) THEN
                    ALTER TABLE {received} ADD CONSTRAINT "ck_messaging_received_inbox_lifecycle" CHECK (
                        NOT "is_inbox_record" OR ("lifecycle_id" IS NOT NULL
                            AND ("replay_parent_incarnation_id" IS NOT NULL OR "lifecycle_id" = "generation_incarnation_id"))
                    );
                END IF;
            END
            $headless$;

            CREATE UNIQUE INDEX IF NOT EXISTS "uq_messaging_received_inbox_root_key" ON {received}
                ("tenant_present","tenant_id","message_id","intent_type","contract_identity","contract_version","consumer_identity","generation")
                WHERE "is_inbox_record" AND "replay_parent_incarnation_id" IS NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS "uq_messaging_received_inbox_lifecycle_generation" ON {received}
                ("lifecycle_id","generation") WHERE "is_inbox_record";
            CREATE UNIQUE INDEX IF NOT EXISTS "uq_messaging_received_generation_incarnation" ON {received} ("generation_incarnation_id")
                WHERE "generation_incarnation_id" IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS "uq_messaging_received_non_inbox_consumer_identity" ON {received}
                ("version","message_id","consumer_identity","intent_type") WHERE NOT "is_inbox_record";
            CREATE INDEX IF NOT EXISTS "idx_messaging_received_inbox_retention" ON {received} ("effective_expires_at","id")
                INCLUDE ("status_name","next_retry_at","intent_type") WHERE "is_inbox_record" AND NOT "is_held";
            CREATE INDEX IF NOT EXISTS "idx_messaging_received_expires_at_status_name" ON {received} ("expires_at","status_name");
            CREATE INDEX IF NOT EXISTS "idx_messaging_received_version_expires_at_status_name" ON {received} ("version","expires_at","status_name");
            CREATE INDEX IF NOT EXISTS "idx_messaging_received_delayed" ON {received} ("status_name","expires_at") WHERE "status_name" = 'Delayed';
            -- #508 — ("status_name","added") serves BOTH the dashboard hourly-timeline query
            -- (WHERE "status_name"=$1 AND "added" BETWEEN … — a status_name seek + added range scan) and the
            -- per-status COUNTs in GetStatisticsAsync via its "status_name" prefix. The step creates the final
            -- schema directly; it does not carry migration DDL for superseded index shapes.
            CREATE INDEX IF NOT EXISTS "idx_messaging_received_status_name_added" ON {received} ("status_name","added");

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


            -- CREATE TABLE IF NOT EXISTS skips a table that already exists in another shape (created by an
            -- older binary or by hand), so the inbox contract the runtime depends on is asserted here: a
            -- missing key index, constraint, or column fails the step instead of the first inbox write.
            DO $inbox_readiness$
            BEGIN
                IF (
                    SELECT COUNT(1) FROM pg_indexes
                    WHERE schemaname = '{schema}'
                      AND indexname IN ('uq_messaging_received_inbox_root_key','uq_messaging_received_inbox_lifecycle_generation')
                ) <> 2 THEN
                    RAISE EXCEPTION 'Headless.Messaging inbox schema is incomplete: the final inbox key index is missing.';
                END IF;

                IF NOT (
                    EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_messaging_received_inbox_identity'
                        AND conrelid = '{received}'::regclass)
                    AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_messaging_received_inbox_lifecycle'
                        AND conrelid = '{received}'::regclass)
                    AND EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_schema='{schema}' AND table_name='messaging_received' AND column_name='lifecycle_id')
                    AND EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_schema='{schema}' AND table_name='messaging_inbox_operation_receipts' AND column_name='expected_status')
                    AND EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_schema='{schema}' AND table_name='messaging_inbox_operation_receipts' AND column_name='outcome')
                    AND EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_schema='{schema}' AND table_name='messaging_inbox_operation_receipts' AND column_name='target_kind')
                    AND EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_schema='{schema}' AND table_name='messaging_inbox_operation_receipts' AND column_name='expected_due_at')
                    AND EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_schema='{schema}' AND table_name='messaging_inbox_audit' AND column_name='target_kind')
                ) THEN
                    RAISE EXCEPTION 'Headless.Messaging inbox schema is incomplete: the lifecycle, retention or operation receipt contract is missing.';
                END IF;
            END
            $inbox_readiness$;
            """
        );
    }

    private static string _PublishedTableSql(string schema, int ownerColumnMaxLength)
    {
        var published = PostgreSqlStorageTableNames.Published(schema);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"""
            CREATE TABLE IF NOT EXISTS {published}(
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
                "owner" VARCHAR({ownerColumnMaxLength}) NULL,
            	"status_name" VARCHAR(50) NOT NULL,
                "message_id" VARCHAR(200) NOT NULL
            );

            CREATE INDEX IF NOT EXISTS "idx_messaging_published_expires_at_status_name" ON {published}("expires_at","status_name");
            CREATE INDEX IF NOT EXISTS "idx_messaging_published_version_expires_at_status_name" ON {published} ("version","expires_at","status_name");
            CREATE INDEX IF NOT EXISTS "idx_messaging_published_delayed" ON {published} ("status_name","expires_at") WHERE "status_name" = 'Delayed';
            -- #509 — partial index for the Queued branch of ScheduleMessagesOfDelayedAsync's OR predicate
            -- (WHERE "version"=$1 AND ("expires_at"<$2 AND "status_name"='Queued')). Leading with
            -- ("version","expires_at") gives a version seek + expires_at range scan, which the planner can
            -- bitmap-OR with the Delayed partial index above instead of sequentially scanning a large
            -- Queued backlog (e.g. accumulated during broker downtime).
            CREATE INDEX IF NOT EXISTS "idx_messaging_published_version_expires_at_queued" ON {published} ("version","expires_at") WHERE "status_name" = 'Queued';
            -- #508 — see the received-table note above; create the final dashboard timeline/statistics index.
            CREATE INDEX IF NOT EXISTS "idx_messaging_published_status_name_added" ON {published} ("status_name","added");
            """
        );
    }

    private static string _PickupIndexesSql(string schema, bool inbox)
    {
        var received = PostgreSqlStorageTableNames.Received(schema);
        var published = PostgreSqlStorageTableNames.Published(schema);

        // #8 — the partial retry-pickup indexes serve the hot retry-pickup path, the owner indexes serve dead-owner
        // reclaim, and the history indexes serve retention selection and audit-reference lookups. They are built
        // with a plain CREATE INDEX inside the step transaction: the runner applies a step once, when the tables
        // are fresh, so the brief write lock a non-concurrent build takes never meets a large backlog.
        var publishedSql = $"""
            CREATE INDEX IF NOT EXISTS "idx_messaging_published_version_next_retry_at" ON {published} ("version","intent_type","next_retry_at") INCLUDE ("retries","locked_until") WHERE "next_retry_at" IS NOT NULL;
            CREATE INDEX IF NOT EXISTS "idx_messaging_published_owner_not_null" ON {published} ("owner") WHERE "owner" IS NOT NULL;

            """;

        if (!inbox)
        {
            return publishedSql;
        }

        return publishedSql
            + $"""
                CREATE INDEX IF NOT EXISTS "idx_messaging_received_version_next_retry_at" ON {received} ("version","intent_type","next_retry_at") INCLUDE ("retries","locked_until") WHERE "next_retry_at" IS NOT NULL;
                CREATE INDEX IF NOT EXISTS "idx_messaging_received_owner_not_null" ON {received} ("owner") WHERE "owner" IS NOT NULL;

                CREATE INDEX IF NOT EXISTS "idx_messaging_inbox_receipts_type_created" ON "{schema}"."messaging_inbox_operation_receipts" ("operation_type","created_at");
                CREATE INDEX IF NOT EXISTS "idx_messaging_inbox_audit_type_created" ON "{schema}"."messaging_inbox_audit" ("operation_type","created_at");
                CREATE INDEX IF NOT EXISTS "idx_messaging_inbox_audit_operation" ON "{schema}"."messaging_inbox_audit" ("operation_id");
                """;
    }

    private static string _ContentSearchSql(string schema, bool inbox)
    {
        var received = PostgreSqlStorageTableNames.Received(schema);
        var published = PostgreSqlStorageTableNames.Published(schema);
        var receivedIndex = inbox
            ? $"""CREATE INDEX IF NOT EXISTS "idx_messaging_received_content_trgm" ON {received} USING gin ("content" gin_trgm_ops);"""
            : "";

        // #507 — CREATE EXTENSION needs superuser or an elevated role that managed PostgreSQL (RDS, Azure, Neon,
        // Supabase) withholds, and a self-hosted server may not ship the pg_trgm contrib package at all. The
        // extension only powers the dashboard trigram (ILIKE) content search, never a write or retry-pickup path, so
        // its failure must not fail the step: the exception block runs it in a subtransaction, which rolls back only
        // the CREATE EXTENSION and leaves the step transaction usable. The index block then checks pg_extension,
        // because a DBA may have pre-installed pg_trgm even when this role cannot create it. When it is absent the
        // trigram indexes are skipped and dashboard content search stays off; this step is recorded either way, so
        // installing pg_trgm afterwards means creating the two indexes by hand.
        return $"""
            DO $headless_trgm$
            BEGIN
                CREATE EXTENSION IF NOT EXISTS pg_trgm;
            EXCEPTION WHEN OTHERS THEN
                RAISE WARNING 'Headless.Messaging could not ensure the pg_trgm extension (SQLSTATE %: %). Dashboard content (ILIKE) search is unavailable until a DBA installs pg_trgm; messaging write and retry paths are unaffected.', SQLSTATE, SQLERRM;
            END
            $headless_trgm$;

            DO $headless_trgm_indexes$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_trgm') THEN
                    {receivedIndex}
                    CREATE INDEX IF NOT EXISTS "idx_messaging_published_content_trgm" ON {published} USING gin ("content" gin_trgm_ops);
                ELSE
                    RAISE WARNING 'pg_trgm is not installed; Headless.Messaging skipped the dashboard content trigram indexes. Messaging write and retry paths are unaffected.';
                END IF;
            END
            $headless_trgm_indexes$;
            """;
    }
}
