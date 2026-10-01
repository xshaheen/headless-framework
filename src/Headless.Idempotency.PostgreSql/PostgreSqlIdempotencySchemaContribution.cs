// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
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
        var pending = IdempotencyTable.StatusLiteral(IdempotencyRecordStatus.Pending);
        var completed = IdempotencyTable.StatusLiteral(IdempotencyRecordStatus.Completed);

        string name(string pascal) => dialect.Quote(dialect.Name(pascal));

        // Key columns compare with the "C" collation, so keys and tenant ids match ordinally (byte for byte)
        // whatever the database's default collation is. A completed record always carries its result and contract
        // and a pending one never does, so a replay can never read a half-written outcome. A pending record names
        // an admitted attempt's generation exactly when it carries that attempt's lease expiry; a completed one
        // keeps the generation that completed it and no lease. A recovery point is whole (name, state, and
        // contract) or absent, and only a pending record keeps one, since completion clears it. One store-wide
        // sequence issues every generation, so a key admitted again after its record was purged still gets a
        // generation above every earlier attempt's. The retention index serves the purge.
        var sql = $"""
            CREATE SEQUENCE IF NOT EXISTS {t.Sequence} AS bigint START WITH 1 INCREMENT BY 1 NO CYCLE;

            CREATE TABLE IF NOT EXISTS {t.Table} (
                {t.TenantId} varchar({IdempotencyFieldLimits.TenantIdMaxLength}) COLLATE "C" NOT NULL,
                {t.Key} varchar({IdempotencyFieldLimits.KeyMaxLength}) COLLATE "C" NOT NULL,
                {t.Status} smallint NOT NULL,
                {t.FingerprintAlgorithm} varchar({IdempotencyFieldLimits.FingerprintAlgorithmMaxLength}) COLLATE "C" NOT NULL,
                {t.Fingerprint} bytea NOT NULL,
                {t.Generation} bigint NULL,
                {t.LeaseExpiresAt} timestamptz NULL,
                {t.Result} bytea NULL,
                {t.ResultContract} varchar({IdempotencyFieldLimits.ContractMaxLength}) COLLATE "C" NULL,
                {t.RetentionUntil} timestamptz NOT NULL,
                {t.RecoveryPoint} varchar({IdempotencyFieldLimits.RecoveryPointMaxLength}) COLLATE "C" NULL,
                {t.RecoveryState} bytea NULL,
                {t.RecoveryContract} varchar({IdempotencyFieldLimits.ContractMaxLength}) COLLATE "C" NULL,
                CONSTRAINT {name("PK_IdempotencyRecords")} PRIMARY KEY ({t.TenantId}, {t.Key}),
                CONSTRAINT {name("CK_IdempotencyRecords_Status")} CHECK ({t.Status} BETWEEN {pending} AND {completed}),
                CONSTRAINT {name("CK_IdempotencyRecords_Fingerprint")} CHECK (
                    octet_length({t.Fingerprint}) BETWEEN 1 AND {IdempotencyFieldLimits.FingerprintMaxLength}
                ),
                CONSTRAINT {name("CK_IdempotencyRecords_Result")} CHECK (
                    ({t.Status} = {completed}) = ({t.Result} IS NOT NULL AND {t.ResultContract} IS NOT NULL)
                    AND ({t.Result} IS NULL) = ({t.ResultContract} IS NULL)
                ),
                CONSTRAINT {name("CK_IdempotencyRecords_Recovery")} CHECK (
                    ({t.RecoveryPoint} IS NULL) = ({t.RecoveryState} IS NULL)
                    AND ({t.RecoveryPoint} IS NULL) = ({t.RecoveryContract} IS NULL)
                    AND ({t.RecoveryPoint} IS NULL OR {t.Status} = {pending})
                    AND COALESCE(octet_length({t.RecoveryState}), 0) <= {IdempotencyFieldLimits.RecoveryStateMaxLength}
                ),
                CONSTRAINT {name("CK_IdempotencyRecords_Lease")} CHECK (
                    ({t.Generation} IS NULL OR {t.Generation} > 0)
                    AND CASE {t.Status}
                        WHEN {completed} THEN {t.Generation} IS NOT NULL AND {t.LeaseExpiresAt} IS NULL
                        ELSE ({t.Generation} IS NULL) = ({t.LeaseExpiresAt} IS NULL)
                    END
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
            applyOnStartup: options.InitializeOnStartup
        );
    }
}
