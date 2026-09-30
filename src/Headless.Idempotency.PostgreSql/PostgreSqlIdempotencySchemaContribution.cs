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
        PostgreSqlIdempotencyOptions options,
        IdempotencyStorageOptions storageOptions
    )
    {
        var schema = storageOptions.Schema;
        var table = PostgreSqlIdempotencySchema.QualifiedTable(schema);
        var sequence = PostgreSqlIdempotencySchema.QualifiedSequence(schema);
        const string t = PostgreSqlIdempotencySchema.TableName;

        // Key columns compare with the "C" collation, so keys and tenant ids match ordinally (byte for byte)
        // whatever the database's default collation is. A completed record always carries its result and contract
        // and a pending one never does, so a replay can never read a half-written outcome. A pending record names
        // an admitted attempt's generation exactly when it carries that attempt's lease expiry; a completed one
        // keeps the generation that completed it and no lease. A recovery point is whole (name, state, and
        // contract) or absent, and only a pending record keeps one, since completion clears it. One store-wide
        // sequence issues every generation, so a key admitted again after its record was purged still gets a
        // generation above every earlier attempt's. The retention index serves the purge.
        var sql = $"""
            CREATE SEQUENCE IF NOT EXISTS {sequence} AS bigint START WITH 1 INCREMENT BY 1 NO CYCLE;

            CREATE TABLE IF NOT EXISTS {table} (
                {PostgreSqlIdempotencySchema.TenantId} varchar({IdempotencyFieldLimits.TenantIdMaxLength}) COLLATE "C" NOT NULL,
                {PostgreSqlIdempotencySchema.Key} varchar({IdempotencyFieldLimits.KeyMaxLength}) COLLATE "C" NOT NULL,
                {PostgreSqlIdempotencySchema.Status} smallint NOT NULL,
                {PostgreSqlIdempotencySchema.FingerprintAlgorithm} varchar({IdempotencyFieldLimits.FingerprintAlgorithmMaxLength}) COLLATE "C" NOT NULL,
                {PostgreSqlIdempotencySchema.Fingerprint} bytea NOT NULL,
                {PostgreSqlIdempotencySchema.Generation} bigint NULL,
                {PostgreSqlIdempotencySchema.LeaseExpiresAt} timestamptz NULL,
                {PostgreSqlIdempotencySchema.Result} bytea NULL,
                {PostgreSqlIdempotencySchema.ResultContract} varchar({IdempotencyFieldLimits.ContractMaxLength}) COLLATE "C" NULL,
                {PostgreSqlIdempotencySchema.RetentionUntil} timestamptz NOT NULL,
                {PostgreSqlIdempotencySchema.RecoveryPoint} varchar({IdempotencyFieldLimits.RecoveryPointMaxLength}) COLLATE "C" NULL,
                {PostgreSqlIdempotencySchema.RecoveryState} bytea NULL,
                {PostgreSqlIdempotencySchema.RecoveryContract} varchar({IdempotencyFieldLimits.ContractMaxLength}) COLLATE "C" NULL,
                CONSTRAINT "pk_{t}" PRIMARY KEY (
                    {PostgreSqlIdempotencySchema.TenantId},
                    {PostgreSqlIdempotencySchema.Key}
                ),
                CONSTRAINT "ck_{t}_status" CHECK (
                    {PostgreSqlIdempotencySchema.Status} BETWEEN {PostgreSqlIdempotencySchema.Pending} AND {PostgreSqlIdempotencySchema.Completed}
                ),
                CONSTRAINT "ck_{t}_fingerprint" CHECK (
                    octet_length({PostgreSqlIdempotencySchema.Fingerprint}) BETWEEN 1 AND {IdempotencyFieldLimits.FingerprintMaxLength}
                ),
                CONSTRAINT "ck_{t}_result" CHECK (
                    ({PostgreSqlIdempotencySchema.Status} = {PostgreSqlIdempotencySchema.Completed})
                        = ({PostgreSqlIdempotencySchema.Result} IS NOT NULL AND {PostgreSqlIdempotencySchema.ResultContract} IS NOT NULL)
                    AND ({PostgreSqlIdempotencySchema.Result} IS NULL) = ({PostgreSqlIdempotencySchema.ResultContract} IS NULL)
                ),
                CONSTRAINT "ck_{t}_recovery" CHECK (
                    ({PostgreSqlIdempotencySchema.RecoveryPoint} IS NULL) = ({PostgreSqlIdempotencySchema.RecoveryState} IS NULL)
                    AND ({PostgreSqlIdempotencySchema.RecoveryPoint} IS NULL) = ({PostgreSqlIdempotencySchema.RecoveryContract} IS NULL)
                    AND ({PostgreSqlIdempotencySchema.RecoveryPoint} IS NULL OR {PostgreSqlIdempotencySchema.Status} = {PostgreSqlIdempotencySchema.Pending})
                    AND COALESCE(octet_length({PostgreSqlIdempotencySchema.RecoveryState}), 0) <= {IdempotencyFieldLimits.RecoveryStateMaxLength}
                ),
                CONSTRAINT "ck_{t}_lease" CHECK (
                    ({PostgreSqlIdempotencySchema.Generation} IS NULL OR {PostgreSqlIdempotencySchema.Generation} > 0)
                    AND CASE {PostgreSqlIdempotencySchema.Status}
                        WHEN {PostgreSqlIdempotencySchema.Completed} THEN
                            {PostgreSqlIdempotencySchema.Generation} IS NOT NULL AND {PostgreSqlIdempotencySchema.LeaseExpiresAt} IS NULL
                        ELSE
                            ({PostgreSqlIdempotencySchema.Generation} IS NULL) = ({PostgreSqlIdempotencySchema.LeaseExpiresAt} IS NULL)
                    END
                )
            );

            CREATE INDEX IF NOT EXISTS "ix_{t}_retention_until"
                ON {table} ({PostgreSqlIdempotencySchema.RetentionUntil});
            """;

        return new SchemaContribution(
            feature: "Idempotency",
            dialect: PostgreSqlSchemaDialect.Instance,
            createConnection: options.CreateConnection,
            schema: schema,
            steps:
            [
                new SchemaStep(StepVersion, "Create the generation sequence, record table, and retention index.", sql),
            ],
            applyOnStartup: options.InitializeOnStartup
        );
    }
}
