// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Hosting.Schema;
using Headless.Sql.PostgreSql;

namespace Headless.Idempotency.PostgreSql;

/// <summary>
/// The Idempotency feature's schema contribution for PostgreSQL: the generation sequence and record table, as one
/// idempotent step the Headless schema runner applies.
/// </summary>
internal static class PostgreSqlIdempotencySchemaContribution
{
    public const string StepVersion = "1";

    public static SchemaContribution Create(
        RelationalIdempotencyOptions options,
        IdempotencyStorageOptions storageOptions
    )
    {
        var dialect = PostgreSqlDialect.Instance;
        var t = new IdempotencyTable(dialect, storageOptions.Schema);
        var idle = IdempotencyTable.StateLiteral(RowLeaseState.Idle);
        var active = IdempotencyTable.StateLiteral(RowLeaseState.Active);
        var settled = IdempotencyTable.StateLiteral(RowLeaseState.Settled);
        var released = IdempotencyTable.StateLiteral(RowLeaseState.Released);

        string name(string pascal) => dialect.Quote(dialect.Name(pascal));

        // Key columns compare with the "C" collation, so keys and tenant ids match ordinally (byte for byte)
        // whatever the database's default collation is. The five lease columns are a fenced row's: a record is
        // idle at generation 0 until its first admission, active while an attempt holds it, settled once completed,
        // and released when an attempt gave it up; the store never abandons or revokes a record. A settled record
        // always carries its result and contract and any other never does, so a replay can never read a
        // half-written outcome. A recovery point is whole (name, state, and contract) or absent, and a settled record
        // never keeps one, since completion clears it. One store-wide sequence issues every generation, so a key
        // admitted again after its record was purged still gets a generation above every earlier attempt's. The
        // retention index serves the purge.
        var sql = $"""
            CREATE SEQUENCE IF NOT EXISTS {t.Sequence} AS bigint START WITH 1 INCREMENT BY 1 NO CYCLE;

            CREATE TABLE IF NOT EXISTS {t.Table} (
                {t.TenantId} varchar({IdempotencyFieldLimits.TenantIdMaxLength}) COLLATE "C" NOT NULL,
                {t.Key} varchar({IdempotencyFieldLimits.KeyMaxLength}) COLLATE "C" NOT NULL,
                {t.FingerprintAlgorithm} varchar({IdempotencyFieldLimits.FingerprintAlgorithmMaxLength}) COLLATE "C" NOT NULL,
                {t.Fingerprint} bytea NOT NULL,
                {t.LeaseGeneration} bigint NOT NULL DEFAULT 0,
                {t.LeaseState} smallint NOT NULL DEFAULT {idle},
                {t.LeaseExpiresAt} timestamptz NULL,
                {t.LeaseHolder} varchar({FencingFieldLimits.HolderMaxLength}) NULL,
                {t.LeaseTakeoverCount} integer NOT NULL DEFAULT 0,
                {t.Result} bytea NULL,
                {t.ResultContract} varchar({IdempotencyFieldLimits.ContractMaxLength}) COLLATE "C" NULL,
                {t.RetentionUntil} timestamptz NOT NULL,
                {t.RecoveryPoint} varchar({IdempotencyFieldLimits.RecoveryPointMaxLength}) COLLATE "C" NULL,
                {t.RecoveryState} bytea NULL,
                {t.RecoveryContract} varchar({IdempotencyFieldLimits.ContractMaxLength}) COLLATE "C" NULL,
                CONSTRAINT {name("PK_IdempotencyRecords")} PRIMARY KEY ({t.TenantId}, {t.Key}),
                CONSTRAINT {name(
                "CK_IdempotencyRecords_LeaseState"
            )} CHECK ({t.LeaseState} BETWEEN {idle} AND {released}),
                CONSTRAINT {name("CK_IdempotencyRecords_Lease")} CHECK (
                    {t.LeaseGeneration} >= 0
                    AND ({t.LeaseState} = {idle}) = ({t.LeaseGeneration} = 0)
                    AND ({t.LeaseState} <> {active} OR {t.LeaseExpiresAt} IS NOT NULL)
                    AND {t.LeaseTakeoverCount} >= 0
                ),
                CONSTRAINT {name("CK_IdempotencyRecords_Fingerprint")} CHECK (
                    octet_length({t.Fingerprint}) BETWEEN 1 AND {IdempotencyFieldLimits.FingerprintMaxLength}
                ),
                CONSTRAINT {name("CK_IdempotencyRecords_Result")} CHECK (
                    ({t.LeaseState} = {settled}) = ({t.Result} IS NOT NULL AND {t.ResultContract} IS NOT NULL)
                    AND ({t.Result} IS NULL) = ({t.ResultContract} IS NULL)
                ),
                CONSTRAINT {name("CK_IdempotencyRecords_Recovery")} CHECK (
                    ({t.RecoveryPoint} IS NULL) = ({t.RecoveryState} IS NULL)
                    AND ({t.RecoveryPoint} IS NULL) = ({t.RecoveryContract} IS NULL)
                    AND ({t.RecoveryPoint} IS NULL OR {t.LeaseState} <> {settled})
                    AND COALESCE(octet_length({t.RecoveryState}), 0) <= {IdempotencyFieldLimits.RecoveryStateMaxLength}
                )
            );

            CREATE INDEX IF NOT EXISTS {name("IX_IdempotencyRecords_RetentionUntil")} ON {t.Table} ({t.RetentionUntil});
            """;

        return new SchemaContribution(
            feature: "Idempotency",
            dialect: PostgreSqlSchemaDialect.Instance,
            createConnection: () => dialect.CreateConnection(options.ConnectionString),
            schema: storageOptions.Schema,
            steps:
            [
                new SchemaStep(StepVersion, "Create the generation sequence, record table, and retention index.", sql),
            ],
            objects: [SchemaObject.Sequence(t.SequenceName), SchemaObject.Table(t.TableName)]
        );
    }
}
