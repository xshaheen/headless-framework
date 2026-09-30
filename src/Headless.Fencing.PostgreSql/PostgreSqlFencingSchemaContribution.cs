// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization.Schema;
using Headless.Sql.PostgreSql;

namespace Headless.Fencing.PostgreSql;

/// <summary>
/// The Fencing feature's schema contribution for PostgreSQL: the generation sequence, lease table, and its indexes, as
/// one idempotent step the Headless schema runner applies.
/// </summary>
internal static class PostgreSqlFencingSchemaContribution
{
    public const string StepVersion = "1";

    public static SchemaContribution Create(PostgreSqlFencingOptions options, FencingStorageOptions storageOptions)
    {
        var schema = storageOptions.Schema;
        var table = PostgreSqlFencingSchema.QualifiedTable(schema);
        var sequence = PostgreSqlFencingSchema.QualifiedSequence(schema);
        const string t = PostgreSqlFencingSchema.TableName;

        // Key columns compare with the "C" collation, so kinds, resources, and tenant ids match ordinally (byte for
        // byte) whatever the database's default collation is. One store-wide sequence issues every generation, so a
        // lease granted again after its row was purged still gets a generation above every earlier one. The active
        // index serves the sweep's keyset walk in (expires_at, tenant_id, resource) order; the ended index serves
        // purge. Progress and its contract are stored together or not at all.
        var sql = $"""
            CREATE SEQUENCE IF NOT EXISTS {sequence} AS bigint START WITH 1 INCREMENT BY 1 NO CYCLE;

            CREATE TABLE IF NOT EXISTS {table} (
                {PostgreSqlFencingSchema.TenantId} varchar({FencingFieldLimits.TenantIdMaxLength}) COLLATE "C" NOT NULL,
                {PostgreSqlFencingSchema.Kind} varchar({FencingFieldLimits.KindMaxLength}) COLLATE "C" NOT NULL,
                {PostgreSqlFencingSchema.Resource} varchar({FencingFieldLimits.ResourceMaxLength}) COLLATE "C" NOT NULL,
                {PostgreSqlFencingSchema.Generation} bigint NOT NULL,
                {PostgreSqlFencingSchema.State} smallint NOT NULL,
                {PostgreSqlFencingSchema.GrantedAt} timestamptz NOT NULL,
                {PostgreSqlFencingSchema.ExpiresAt} timestamptz NOT NULL,
                {PostgreSqlFencingSchema.EndedAt} timestamptz NULL,
                {PostgreSqlFencingSchema.TakeoverCount} integer NOT NULL DEFAULT 0,
                {PostgreSqlFencingSchema.Progress} bytea NULL,
                {PostgreSqlFencingSchema.ProgressContract} varchar({FencingFieldLimits.ProgressContractMaxLength}) NULL,
                CONSTRAINT "pk_{t}" PRIMARY KEY (
                    {PostgreSqlFencingSchema.TenantId},
                    {PostgreSqlFencingSchema.Kind},
                    {PostgreSqlFencingSchema.Resource}
                ),
                CONSTRAINT "ck_{t}_generation" CHECK ({PostgreSqlFencingSchema.Generation} > 0),
                CONSTRAINT "ck_{t}_state" CHECK (
                    {PostgreSqlFencingSchema.State} BETWEEN {PostgreSqlFencingSchema.Active} AND {PostgreSqlFencingSchema.Abandoned}
                ),
                CONSTRAINT "ck_{t}_ended_at" CHECK (
                    ({PostgreSqlFencingSchema.State} = {PostgreSqlFencingSchema.Active}) = ({PostgreSqlFencingSchema.EndedAt} IS NULL)
                ),
                CONSTRAINT "ck_{t}_takeover_count" CHECK ({PostgreSqlFencingSchema.TakeoverCount} >= 0),
                CONSTRAINT "ck_{t}_progress" CHECK (
                    ({PostgreSqlFencingSchema.Progress} IS NULL) = ({PostgreSqlFencingSchema.ProgressContract} IS NULL)
                )
            );

            CREATE INDEX IF NOT EXISTS "ix_{t}_active_expiry"
                ON {table} (
                    {PostgreSqlFencingSchema.Kind},
                    {PostgreSqlFencingSchema.ExpiresAt},
                    {PostgreSqlFencingSchema.TenantId},
                    {PostgreSqlFencingSchema.Resource}
                )
                WHERE {PostgreSqlFencingSchema.State} = {PostgreSqlFencingSchema.Active};

            CREATE INDEX IF NOT EXISTS "ix_{t}_ended"
                ON {table} ({PostgreSqlFencingSchema.Kind}, {PostgreSqlFencingSchema.EndedAt})
                WHERE {PostgreSqlFencingSchema.State} <> {PostgreSqlFencingSchema.Active};
            """;

        return new SchemaContribution(
            feature: "Fencing",
            dialect: PostgreSqlSchemaDialect.Instance,
            createConnection: options.CreateConnection,
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
