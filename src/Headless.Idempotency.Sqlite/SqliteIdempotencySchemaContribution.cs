// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Headless.Sql.Sqlite;

namespace Headless.Idempotency.Sqlite;

/// <summary>
/// The Idempotency feature's schema contribution for SQLite: the generation sequence, record table, and retention
/// index, as one idempotent step the Headless schema runner applies.
/// </summary>
internal static class SqliteIdempotencySchemaContribution
{
    public const string StepVersion = "1";

    public static SchemaContribution Create(
        RelationalIdempotencyOptions options,
        IdempotencyStorageOptions storageOptions
    )
    {
        var dialect = SqliteDialect.Instance;
        var schema = storageOptions.Schema;
        var t = new IdempotencyTable(dialect, schema);
        var pending = IdempotencyTable.StatusLiteral(IdempotencyRecordStatus.Pending);
        var completed = IdempotencyTable.StatusLiteral(IdempotencyRecordStatus.Completed);

        string name(string pascal) => dialect.Quote(dialect.Name(pascal));

        // Index and trigger names are global to a SQLite file, so they carry the schema prefix like the tables.
        string global(string pascal) => dialect.Quote(SqliteDialect.QualifiedName(schema, dialect.Name(pascal)));

        // The constraints are PostgreSQL's (see its contribution for why each holds); TEXT compares with SQLite's
        // BINARY collation, so keys and tenant ids match ordinally. SQLite has no sequence objects: the sequence is a
        // one-row table holding the last generation issued, which the dialect's next-value expression reads and these
        // triggers advance whenever a generation is written, inside the same statement and under the database write
        // lock. A purge deletes records, never the sequence row, so a key admitted again after its record was purged
        // still gets a generation above every earlier attempt's.
        var sql = $"""
            CREATE TABLE IF NOT EXISTS {t.Sequence} (
                id INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
                value INTEGER NOT NULL CHECK (value >= 0)
            );

            INSERT INTO {t.Sequence} (id, value) VALUES (1, 0) ON CONFLICT (id) DO NOTHING;

            CREATE TABLE IF NOT EXISTS {t.Table} (
                {t.TenantId} TEXT NOT NULL,
                {t.Key} TEXT NOT NULL,
                {t.Status} INTEGER NOT NULL,
                {t.FingerprintAlgorithm} TEXT NOT NULL,
                {t.Fingerprint} BLOB NOT NULL,
                {t.Generation} INTEGER NULL,
                {t.LeaseExpiresAt} TEXT NULL,
                {t.Result} BLOB NULL,
                {t.ResultContract} TEXT NULL,
                {t.RetentionUntil} TEXT NOT NULL,
                {t.RecoveryPoint} TEXT NULL,
                {t.RecoveryState} BLOB NULL,
                {t.RecoveryContract} TEXT NULL,
                CONSTRAINT {name("PK_IdempotencyRecords")} PRIMARY KEY ({t.TenantId}, {t.Key}),
                CONSTRAINT {name("CK_IdempotencyRecords_Status")} CHECK ({t.Status} BETWEEN {pending} AND {completed}),
                CONSTRAINT {name("CK_IdempotencyRecords_Fingerprint")} CHECK (
                    length({t.Fingerprint}) BETWEEN 1 AND {IdempotencyFieldLimits.FingerprintMaxLength}
                ),
                CONSTRAINT {name("CK_IdempotencyRecords_Result")} CHECK (
                    ({t.Status} = {completed}) = ({t.Result} IS NOT NULL AND {t.ResultContract} IS NOT NULL)
                    AND ({t.Result} IS NULL) = ({t.ResultContract} IS NULL)
                ),
                CONSTRAINT {name("CK_IdempotencyRecords_Recovery")} CHECK (
                    ({t.RecoveryPoint} IS NULL) = ({t.RecoveryState} IS NULL)
                    AND ({t.RecoveryPoint} IS NULL) = ({t.RecoveryContract} IS NULL)
                    AND ({t.RecoveryPoint} IS NULL OR {t.Status} = {pending})
                    AND COALESCE(length({t.RecoveryState}), 0) <= {IdempotencyFieldLimits.RecoveryStateMaxLength}
                ),
                CONSTRAINT {name("CK_IdempotencyRecords_Lease")} CHECK (
                    ({t.Generation} IS NULL OR {t.Generation} > 0)
                    AND CASE {t.Status}
                        WHEN {completed} THEN {t.Generation} IS NOT NULL AND {t.LeaseExpiresAt} IS NULL
                        ELSE ({t.Generation} IS NULL) = ({t.LeaseExpiresAt} IS NULL)
                    END
                )
            );

            CREATE INDEX IF NOT EXISTS {global(
                "IX_IdempotencyRecords_RetentionUntil"
            )} ON {t.Table} ({t.RetentionUntil});

            CREATE TRIGGER IF NOT EXISTS {global("TR_IdempotencyRecords_GenerationInserted")}
            AFTER INSERT ON {t.Table}
            WHEN NEW.{t.Generation} > (SELECT value FROM {t.Sequence})
            BEGIN
                UPDATE {t.Sequence} SET value = NEW.{t.Generation} WHERE id = 1;
            END;

            CREATE TRIGGER IF NOT EXISTS {global("TR_IdempotencyRecords_GenerationUpdated")}
            AFTER UPDATE OF {t.Generation} ON {t.Table}
            WHEN NEW.{t.Generation} > (SELECT value FROM {t.Sequence})
            BEGIN
                UPDATE {t.Sequence} SET value = NEW.{t.Generation} WHERE id = 1;
            END;
            """;

        return new SchemaContribution(
            feature: "Idempotency",
            dialect: SqliteSchemaDialect.Instance,
            createConnection: () => dialect.CreateConnection(options.ConnectionString),
            schema: schema,
            steps:
            [
                new SchemaStep(StepVersion, "Create the generation sequence, record table, and retention index.", sql),
            ],
            applyOnStartup: options.InitializeOnStartup
        );
    }
}
