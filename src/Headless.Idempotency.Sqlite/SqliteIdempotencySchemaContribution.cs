// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Hosting.Schema;
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
        var idle = IdempotencyTable.StateLiteral(RowLeaseState.Idle);
        var active = IdempotencyTable.StateLiteral(RowLeaseState.Active);
        var settled = IdempotencyTable.StateLiteral(RowLeaseState.Settled);
        var released = IdempotencyTable.StateLiteral(RowLeaseState.Released);

        string name(string pascal) => dialect.Quote(dialect.Name(pascal));

        // Index and trigger names are global to a SQLite file, so they carry the schema prefix like the tables.
        string global(string pascal) => dialect.Quote(SqliteDialect.QualifiedName(schema, dialect.Name(pascal)));

        // The constraints are PostgreSQL's (see its contribution for why each holds); TEXT compares with SQLite's
        // BINARY collation, so keys and tenant ids match ordinally. SQLite has no sequence objects: the sequence is a
        // one-row table holding the last generation issued, which the dialect's next-value expression reads and these
        // triggers advance whenever a lease generation is written, inside the same statement and under the database write
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
                {t.FingerprintAlgorithm} TEXT NOT NULL,
                {t.Fingerprint} BLOB NOT NULL,
                {t.LeaseGeneration} INTEGER NOT NULL DEFAULT 0,
                {t.LeaseState} INTEGER NOT NULL DEFAULT {idle},
                {t.LeaseExpiresAt} TEXT NULL,
                {t.LeaseHolder} TEXT NULL,
                {t.LeaseTakeoverCount} INTEGER NOT NULL DEFAULT 0,
                {t.Result} BLOB NULL,
                {t.ResultContract} TEXT NULL,
                {t.RetentionUntil} TEXT NOT NULL,
                {t.RecoveryPoint} TEXT NULL,
                {t.RecoveryState} BLOB NULL,
                {t.RecoveryContract} TEXT NULL,
                CONSTRAINT {name("PK_IdempotencyRecords")} PRIMARY KEY ({t.TenantId}, {t.Key}),
                CONSTRAINT {name("CK_IdempotencyRecords_LeaseState")} CHECK ({t.LeaseState} BETWEEN {idle} AND {released}),
                CONSTRAINT {name("CK_IdempotencyRecords_Lease")} CHECK (
                    {t.LeaseGeneration} >= 0
                    AND ({t.LeaseState} = {idle}) = ({t.LeaseGeneration} = 0)
                    AND ({t.LeaseState} <> {active} OR {t.LeaseExpiresAt} IS NOT NULL)
                    AND {t.LeaseTakeoverCount} >= 0
                    AND COALESCE(length({t.LeaseHolder}), 0) <= {FencingFieldLimits.HolderMaxLength}
                ),
                CONSTRAINT {name("CK_IdempotencyRecords_Fingerprint")} CHECK (
                    length({t.Fingerprint}) BETWEEN 1 AND {IdempotencyFieldLimits.FingerprintMaxLength}
                ),
                CONSTRAINT {name("CK_IdempotencyRecords_Result")} CHECK (
                    ({t.LeaseState} = {settled}) = ({t.Result} IS NOT NULL AND {t.ResultContract} IS NOT NULL)
                    AND ({t.Result} IS NULL) = ({t.ResultContract} IS NULL)
                ),
                CONSTRAINT {name("CK_IdempotencyRecords_Recovery")} CHECK (
                    ({t.RecoveryPoint} IS NULL) = ({t.RecoveryState} IS NULL)
                    AND ({t.RecoveryPoint} IS NULL) = ({t.RecoveryContract} IS NULL)
                    AND ({t.RecoveryPoint} IS NULL OR {t.LeaseState} <> {settled})
                    AND COALESCE(length({t.RecoveryState}), 0) <= {IdempotencyFieldLimits.RecoveryStateMaxLength}
                )
            );

            CREATE INDEX IF NOT EXISTS {global(
                "IX_IdempotencyRecords_RetentionUntil"
            )} ON {t.Table} ({t.RetentionUntil});

            CREATE TRIGGER IF NOT EXISTS {global("TR_IdempotencyRecords_GenerationInserted")}
            AFTER INSERT ON {t.Table}
            WHEN NEW.{t.LeaseGeneration} > (SELECT value FROM {t.Sequence})
            BEGIN
                UPDATE {t.Sequence} SET value = NEW.{t.LeaseGeneration} WHERE id = 1;
            END;

            CREATE TRIGGER IF NOT EXISTS {global("TR_IdempotencyRecords_GenerationUpdated")}
            AFTER UPDATE OF {t.LeaseGeneration} ON {t.Table}
            WHEN NEW.{t.LeaseGeneration} > (SELECT value FROM {t.Sequence})
            BEGIN
                UPDATE {t.Sequence} SET value = NEW.{t.LeaseGeneration} WHERE id = 1;
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
            objects:
            [
                SchemaObject.Sequence(SqliteDialect.QualifiedName(schema, t.SequenceName)),
                SchemaObject.Table(SqliteDialect.QualifiedName(schema, t.TableName)),
            ]
        );
    }
}
