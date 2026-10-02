// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Headless.Sql.SqlServer;

namespace Headless.Idempotency.SqlServer;

/// <summary>
/// The Idempotency feature's schema contribution for SQL Server: the generation sequence and record table, as one
/// idempotent step the Headless schema runner applies.
/// </summary>
internal static class SqlServerIdempotencySchemaContribution
{
    public const string StepVersion = "1";

    // Binary code-point order, so keys and tenant ids match case- and accent-sensitively whatever the database's
    // default collation is. It does not stop SQL Server padding trailing spaces before comparing, so 'a' and 'a '
    // would still collide; call validation refuses keys and tenant ids that start or end with whitespace, which is
    // what makes matching ordinal, the same as on PostgreSQL.
    private const string _KeyCollation = "Latin1_General_100_BIN2";

    public static SchemaContribution Create(
        RelationalIdempotencyOptions options,
        IdempotencyStorageOptions storageOptions
    )
    {
        var dialect = SqlServerDialect.Instance;
        var schema = storageOptions.Schema;
        var t = new IdempotencyTable(dialect, schema);
        var pending = IdempotencyTable.StatusLiteral(IdempotencyRecordStatus.Pending);
        var completed = IdempotencyTable.StatusLiteral(IdempotencyRecordStatus.Completed);
        var tableName = $"{schema}.{t.TableName}";
        var sequenceName = $"{schema}.{t.SequenceName}";
        var index = $"IX_{t.TableName}_RetentionUntil";
        const string collation = _KeyCollation;

        string name(string pascal) => dialect.Quote(dialect.Name(pascal));

        // The clustered primary key over exactly (TenantId, IdempotencyKey) is load-bearing, not an index choice: a
        // lock-or-insert's HOLDLOCK read takes its key-range lock on this index, which is what serializes concurrent
        // first admissions of a new key without a duplicate-key error. The key parts total 384 nvarchar characters,
        // under the 900-byte clustered key limit. A completed record always carries its result and contract and a
        // pending one never does. A pending record names an admitted attempt's generation exactly when it carries
        // that attempt's lease expiry; a completed one keeps the generation that completed it and no lease. A
        // recovery point is whole (name, state, and contract) or absent, and only a pending record keeps one. One
        // store-wide sequence issues every generation, so a key admitted again after its record was purged still
        // gets a generation above every earlier attempt's. The retention index serves the purge.
        var sql = $"""
            IF OBJECT_ID(N'{sequenceName}', N'SO') IS NULL
                CREATE SEQUENCE {t.Sequence} AS bigint START WITH 1 INCREMENT BY 1 NO CYCLE;

            IF OBJECT_ID(N'{tableName}', N'U') IS NULL
                CREATE TABLE {t.Table} (
                    {t.TenantId} nvarchar({IdempotencyFieldLimits.TenantIdMaxLength}) COLLATE {collation} NOT NULL,
                    {t.Key} nvarchar({IdempotencyFieldLimits.KeyMaxLength}) COLLATE {collation} NOT NULL,
                    {t.Status} smallint NOT NULL,
                    {t.FingerprintAlgorithm} nvarchar({IdempotencyFieldLimits.FingerprintAlgorithmMaxLength}) COLLATE {collation} NOT NULL,
                    {t.Fingerprint} varbinary({IdempotencyFieldLimits.FingerprintMaxLength}) NOT NULL,
                    {t.Generation} bigint NULL,
                    {t.LeaseExpiresAt} datetimeoffset(7) NULL,
                    {t.Result} varbinary(max) NULL,
                    {t.ResultContract} nvarchar({IdempotencyFieldLimits.ContractMaxLength}) COLLATE {collation} NULL,
                    {t.RetentionUntil} datetimeoffset(7) NOT NULL,
                    {t.RecoveryPoint} nvarchar({IdempotencyFieldLimits.RecoveryPointMaxLength}) COLLATE {collation} NULL,
                    {t.RecoveryState} varbinary(max) NULL,
                    {t.RecoveryContract} nvarchar({IdempotencyFieldLimits.ContractMaxLength}) COLLATE {collation} NULL,
                    CONSTRAINT {name("PK_IdempotencyRecords")} PRIMARY KEY CLUSTERED ({t.TenantId} ASC, {t.Key} ASC),
                    CONSTRAINT {name(
                "CK_IdempotencyRecords_Status"
            )} CHECK ({t.Status} BETWEEN {pending} AND {completed}),
                    CONSTRAINT {name("CK_IdempotencyRecords_Fingerprint")} CHECK (DATALENGTH({t.Fingerprint}) > 0),
                    CONSTRAINT {name("CK_IdempotencyRecords_Result")} CHECK (
                        ({t.Status} = {completed} AND {t.Result} IS NOT NULL AND {t.ResultContract} IS NOT NULL)
                        OR ({t.Status} = {pending} AND {t.Result} IS NULL AND {t.ResultContract} IS NULL)
                    ),
                    CONSTRAINT {name("CK_IdempotencyRecords_Recovery")} CHECK (
                        ({t.RecoveryPoint} IS NULL AND {t.RecoveryState} IS NULL AND {t.RecoveryContract} IS NULL)
                        OR (
                            {t.RecoveryPoint} IS NOT NULL AND {t.RecoveryState} IS NOT NULL AND {t.RecoveryContract} IS NOT NULL
                            AND {t.Status} = {pending}
                            AND DATALENGTH({t.RecoveryState}) <= {IdempotencyFieldLimits.RecoveryStateMaxLength}
                        )
                    ),
                    CONSTRAINT {name(
                "CK_IdempotencyRecords_Generation"
            )} CHECK ({t.Generation} IS NULL OR {t.Generation} > 0),
                    CONSTRAINT {name("CK_IdempotencyRecords_Lease")} CHECK (
                        ({t.Status} = {completed} AND {t.Generation} IS NOT NULL AND {t.LeaseExpiresAt} IS NULL)
                        OR ({t.Status} = {pending} AND {t.Generation} IS NULL AND {t.LeaseExpiresAt} IS NULL)
                        OR ({t.Status} = {pending} AND {t.Generation} IS NOT NULL AND {t.LeaseExpiresAt} IS NOT NULL)
                    )
                );

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'{tableName}') AND name = N'{index}')
                CREATE INDEX {dialect.Quote(index)} ON {t.Table} ({t.RetentionUntil});
            """;

        return new SchemaContribution(
            feature: "Idempotency",
            dialect: SqlServerSchemaDialect.Instance,
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
