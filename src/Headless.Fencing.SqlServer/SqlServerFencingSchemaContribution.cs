// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Headless.Sql.SqlServer;

namespace Headless.Fencing.SqlServer;

/// <summary>
/// The Fencing feature's schema contribution for SQL Server: the generation sequence, lease table, and its indexes, as
/// one idempotent step the Headless schema runner applies.
/// </summary>
internal static class SqlServerFencingSchemaContribution
{
    public const string StepVersion = "1";

    public static SchemaContribution Create(RelationalFencingOptions options, FencingStorageOptions storageOptions)
    {
        var schema = storageOptions.Schema;
        var table = SqlServerFencingSchema.QualifiedTable(schema);
        var sequence = SqlServerFencingSchema.QualifiedSequence(schema);
        var tableName = $"{schema}.{SqlServerFencingSchema.TableName}";
        var sequenceName = $"{schema}.{SqlServerFencingSchema.SequenceName}";
        const string collation = SqlServerFencingSchema.KeyCollation;
        const string t = SqlServerFencingSchema.TableName;

        // The clustered primary key over exactly (TenantId, Kind, Resource) is load-bearing, not an index choice:
        // a grant's HOLDLOCK read takes its key-range lock on this index, which is what serializes concurrent first
        // grants of a new key. The key-part limits total 448 nvarchar characters, under the 900-byte clustered key
        // limit. One store-wide sequence issues every generation, so a lease granted again after its row was purged
        // still gets a generation above every earlier one. The active index serves the sweep's keyset walk in
        // (ExpiresAt, TenantId, Resource) order; the ended index serves purge. Progress and its contract are stored
        // together or not at all.
        var sql = $"""
            IF OBJECT_ID(N'{sequenceName}', N'SO') IS NULL
                CREATE SEQUENCE {sequence} AS bigint START WITH 1 INCREMENT BY 1 NO CYCLE;

            IF OBJECT_ID(N'{tableName}', N'U') IS NULL
                CREATE TABLE {table} (
                    {SqlServerFencingSchema.TenantId} nvarchar({FencingFieldLimits.TenantIdMaxLength}) COLLATE {collation} NOT NULL,
                    {SqlServerFencingSchema.Kind} nvarchar({FencingFieldLimits.KindMaxLength}) COLLATE {collation} NOT NULL,
                    {SqlServerFencingSchema.Resource} nvarchar({FencingFieldLimits.ResourceMaxLength}) COLLATE {collation} NOT NULL,
                    {SqlServerFencingSchema.Generation} bigint NOT NULL,
                    {SqlServerFencingSchema.State} smallint NOT NULL,
                    {SqlServerFencingSchema.GrantedAt} datetimeoffset(7) NOT NULL,
                    {SqlServerFencingSchema.ExpiresAt} datetimeoffset(7) NOT NULL,
                    {SqlServerFencingSchema.EndedAt} datetimeoffset(7) NULL,
                    {SqlServerFencingSchema.TakeoverCount} int NOT NULL CONSTRAINT [DF_{t}_TakeoverCount] DEFAULT 0,
                    {SqlServerFencingSchema.Progress} varbinary(max) NULL,
                    {SqlServerFencingSchema.ProgressContract} nvarchar({FencingFieldLimits.ProgressContractMaxLength}) NULL,
                    CONSTRAINT [PK_{t}] PRIMARY KEY CLUSTERED (
                        {SqlServerFencingSchema.TenantId} ASC,
                        {SqlServerFencingSchema.Kind} ASC,
                        {SqlServerFencingSchema.Resource} ASC
                    ),
                    CONSTRAINT [CK_{t}_Generation] CHECK ({SqlServerFencingSchema.Generation} > 0),
                    CONSTRAINT [CK_{t}_State] CHECK (
                        {SqlServerFencingSchema.State} BETWEEN {SqlServerFencingSchema.Active} AND {SqlServerFencingSchema.Abandoned}
                    ),
                    CONSTRAINT [CK_{t}_EndedAt] CHECK (
                        ({SqlServerFencingSchema.State} = {SqlServerFencingSchema.Active} AND {SqlServerFencingSchema.EndedAt} IS NULL)
                        OR ({SqlServerFencingSchema.State} <> {SqlServerFencingSchema.Active} AND {SqlServerFencingSchema.EndedAt} IS NOT NULL)
                    ),
                    CONSTRAINT [CK_{t}_TakeoverCount] CHECK ({SqlServerFencingSchema.TakeoverCount} >= 0),
                    CONSTRAINT [CK_{t}_Progress] CHECK (
                        ({SqlServerFencingSchema.Progress} IS NULL AND {SqlServerFencingSchema.ProgressContract} IS NULL)
                        OR ({SqlServerFencingSchema.Progress} IS NOT NULL AND {SqlServerFencingSchema.ProgressContract} IS NOT NULL)
                    )
                );

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'{tableName}') AND name = N'IX_{t}_ActiveExpiry')
                CREATE INDEX [IX_{t}_ActiveExpiry]
                    ON {table} (
                        {SqlServerFencingSchema.Kind},
                        {SqlServerFencingSchema.ExpiresAt},
                        {SqlServerFencingSchema.TenantId},
                        {SqlServerFencingSchema.Resource}
                    )
                    WHERE {SqlServerFencingSchema.State} = {SqlServerFencingSchema.Active};

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'{tableName}') AND name = N'IX_{t}_Ended')
                CREATE INDEX [IX_{t}_Ended]
                    ON {table} ({SqlServerFencingSchema.Kind}, {SqlServerFencingSchema.EndedAt})
                    WHERE {SqlServerFencingSchema.State} <> {SqlServerFencingSchema.Active};
            """;

        return new SchemaContribution(
            feature: "Fencing",
            dialect: SqlServerSchemaDialect.Instance,
            createConnection: () => SqlServerDialect.Instance.CreateConnection(options.ConnectionString),
            schema: schema,
            steps:
            [
                new SchemaStep(
                    StepVersion,
                    "Create the generation sequence, lease table, and sweep and purge indexes.",
                    sql
                ),
            ],
            applyOnStartup: options.InitializeOnStartup
        );
    }
}
