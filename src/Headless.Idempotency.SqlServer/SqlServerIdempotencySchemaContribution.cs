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

    public static SchemaContribution Create(
        SqlServerIdempotencyOptions options,
        IdempotencyStorageOptions storageOptions
    )
    {
        var schema = storageOptions.Schema;
        var table = SqlServerIdempotencySchema.QualifiedTable(schema);
        var tableName = $"{schema}.{SqlServerIdempotencySchema.TableName}";
        var sequence = SqlServerIdempotencySchema.QualifiedSequence(schema);
        var sequenceName = $"{schema}.{SqlServerIdempotencySchema.SequenceName}";
        const string collation = SqlServerIdempotencySchema.KeyCollation;
        const string t = SqlServerIdempotencySchema.TableName;

        // The clustered primary key over exactly (TenantId, IdempotencyKey) is load-bearing, not an index choice: a
        // lock-or-insert's HOLDLOCK read takes its key-range lock on this index, which is what serializes concurrent
        // first admissions of a new key without a duplicate-key error. The key parts total 384 nvarchar characters,
        // under the 900-byte clustered key limit. A completed record always carries its result and contract and a
        // pending one never does. A pending record names an admitted attempt's generation exactly when it carries
        // that attempt's lease expiry; a completed one keeps the generation that completed it and no lease. A
        // recovery point is whole (name, state, and contract) or absent, and only a pending record keeps one. One
        // store-wide sequence issues every generation, so a key admitted again after its record was purged still
        // gets a generation above every earlier attempt's. The retention index serves the purge.
        var sql = $$"""
            IF OBJECT_ID(N'{{sequenceName}}', N'SO') IS NULL
                CREATE SEQUENCE {{sequence}} AS bigint START WITH 1 INCREMENT BY 1 NO CYCLE;

            IF OBJECT_ID(N'{{tableName}}', N'U') IS NULL
                CREATE TABLE {{table}} (
                    {{SqlServerIdempotencySchema.TenantId}} nvarchar({{IdempotencyFieldLimits.TenantIdMaxLength}}) COLLATE {{collation}} NOT NULL,
                    {{SqlServerIdempotencySchema.Key}} nvarchar({{IdempotencyFieldLimits.KeyMaxLength}}) COLLATE {{collation}} NOT NULL,
                    {{SqlServerIdempotencySchema.Status}} smallint NOT NULL,
                    {{SqlServerIdempotencySchema.FingerprintAlgorithm}} nvarchar({{IdempotencyFieldLimits.FingerprintAlgorithmMaxLength}}) COLLATE {{collation}} NOT NULL,
                    {{SqlServerIdempotencySchema.Fingerprint}} varbinary({{IdempotencyFieldLimits.FingerprintMaxLength}}) NOT NULL,
                    {{SqlServerIdempotencySchema.Generation}} bigint NULL,
                    {{SqlServerIdempotencySchema.LeaseExpiresAt}} datetimeoffset(7) NULL,
                    {{SqlServerIdempotencySchema.Result}} varbinary(max) NULL,
                    {{SqlServerIdempotencySchema.ResultContract}} nvarchar({{IdempotencyFieldLimits.ContractMaxLength}}) COLLATE {{collation}} NULL,
                    {{SqlServerIdempotencySchema.RetentionUntil}} datetimeoffset(7) NOT NULL,
                    {{SqlServerIdempotencySchema.RecoveryPoint}} nvarchar({{IdempotencyFieldLimits.RecoveryPointMaxLength}}) COLLATE {{collation}} NULL,
                    {{SqlServerIdempotencySchema.RecoveryState}} varbinary(max) NULL,
                    {{SqlServerIdempotencySchema.RecoveryContract}} nvarchar({{IdempotencyFieldLimits.ContractMaxLength}}) COLLATE {{collation}} NULL,
                    CONSTRAINT [PK_{{t}}] PRIMARY KEY CLUSTERED (
                        {{SqlServerIdempotencySchema.TenantId}} ASC,
                        {{SqlServerIdempotencySchema.Key}} ASC
                    ),
                    CONSTRAINT [CK_{{t}}_Status] CHECK (
                        {{SqlServerIdempotencySchema.Status}} BETWEEN {{SqlServerIdempotencySchema.Pending}} AND {{SqlServerIdempotencySchema.Completed}}
                    ),
                    CONSTRAINT [CK_{{t}}_Fingerprint] CHECK (DATALENGTH({{SqlServerIdempotencySchema.Fingerprint}}) > 0),
                    CONSTRAINT [CK_{{t}}_Result] CHECK (
                        ({{SqlServerIdempotencySchema.Status}} = {{SqlServerIdempotencySchema.Completed}} AND {{SqlServerIdempotencySchema.Result}} IS NOT NULL AND {{SqlServerIdempotencySchema.ResultContract}} IS NOT NULL)
                        OR ({{SqlServerIdempotencySchema.Status}} = {{SqlServerIdempotencySchema.Pending}} AND {{SqlServerIdempotencySchema.Result}} IS NULL AND {{SqlServerIdempotencySchema.ResultContract}} IS NULL)
                    ),
                    CONSTRAINT [CK_{{t}}_Recovery] CHECK (
                        ({{SqlServerIdempotencySchema.RecoveryPoint}} IS NULL AND {{SqlServerIdempotencySchema.RecoveryState}} IS NULL AND {{SqlServerIdempotencySchema.RecoveryContract}} IS NULL)
                        OR (
                            {{SqlServerIdempotencySchema.RecoveryPoint}} IS NOT NULL AND {{SqlServerIdempotencySchema.RecoveryState}} IS NOT NULL AND {{SqlServerIdempotencySchema.RecoveryContract}} IS NOT NULL
                            AND {{SqlServerIdempotencySchema.Status}} = {{SqlServerIdempotencySchema.Pending}}
                            AND DATALENGTH({{SqlServerIdempotencySchema.RecoveryState}}) <= {{IdempotencyFieldLimits.RecoveryStateMaxLength}}
                        )
                    ),
                    CONSTRAINT [CK_{{t}}_Generation] CHECK ({{SqlServerIdempotencySchema.Generation}} IS NULL OR {{SqlServerIdempotencySchema.Generation}} > 0),
                    CONSTRAINT [CK_{{t}}_Lease] CHECK (
                        ({{SqlServerIdempotencySchema.Status}} = {{SqlServerIdempotencySchema.Completed}} AND {{SqlServerIdempotencySchema.Generation}} IS NOT NULL AND {{SqlServerIdempotencySchema.LeaseExpiresAt}} IS NULL)
                        OR ({{SqlServerIdempotencySchema.Status}} = {{SqlServerIdempotencySchema.Pending}} AND {{SqlServerIdempotencySchema.Generation}} IS NULL AND {{SqlServerIdempotencySchema.LeaseExpiresAt}} IS NULL)
                        OR ({{SqlServerIdempotencySchema.Status}} = {{SqlServerIdempotencySchema.Pending}} AND {{SqlServerIdempotencySchema.Generation}} IS NOT NULL AND {{SqlServerIdempotencySchema.LeaseExpiresAt}} IS NOT NULL)
                    )
                );

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'{{tableName}}') AND name = N'IX_{{t}}_RetentionUntil')
                CREATE INDEX [IX_{{t}}_RetentionUntil] ON {{table}} ({{SqlServerIdempotencySchema.RetentionUntil}});
            """;

        return new SchemaContribution(
            feature: "Idempotency",
            dialect: SqlServerSchemaDialect.Instance,
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
