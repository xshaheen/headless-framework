// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Schema;
using Headless.Sql.PostgreSql;

namespace Headless.Encryption.PostgreSql;

/// <summary>The Encryption feature's schema contribution for PostgreSQL: the data-key table, as one step.</summary>
internal static class PostgreSqlEncryptionSchemaContribution
{
    public const string StepVersion = "1";

    public static SchemaContribution Create(PostgreSqlEncryptionKeyStoreOptions options)
    {
        var dialect = PostgreSqlDialect.Instance;
        var table = dialect.Qualify(options.Schema, options.TableName);
        var c = new DataKeyColumns(dialect);

        // The scope compares with the "C" collation, so tenant ids match ordinally (byte for byte) whatever the
        // database's default collation is. The unique (scope, kind, version) constraint is what makes racing creators
        // of a scope's first key agree on one row.
        var sql = $"""
            CREATE TABLE IF NOT EXISTS {table} (
                {c.KeyId} uuid NOT NULL,
                {c.Scope} varchar({DataKeyFieldLimits.ScopeMaxLength}) COLLATE "C" NOT NULL,
                {c.Kind} smallint NOT NULL,
                {c.Version} integer NOT NULL,
                {c.KekProvider} varchar({DataKeyFieldLimits.KekProviderMaxLength}) NOT NULL,
                {c.KekId} varchar({DataKeyFieldLimits.KekIdMaxLength}) NOT NULL,
                {c.WrappedKey} bytea NOT NULL,
                {c.CreatedAt} timestamptz NOT NULL,
                CONSTRAINT {dialect.Quote("pk_" + options.TableName)} PRIMARY KEY ({c.KeyId}),
                CONSTRAINT {dialect.Quote(
                "uq_" + options.TableName + "_version"
            )} UNIQUE ({c.Scope}, {c.Kind}, {c.Version})
            );
            """;

        return new SchemaContribution(
            feature: SchemaContribution.FeatureId(
                "Encryption",
                (options.TableName, PostgreSqlEncryptionKeyStoreOptions.DefaultTableName)
            ),
            dialect: PostgreSqlSchemaDialect.Instance,
            createConnection: () => dialect.CreateConnection(options.ConnectionString),
            schema: options.Schema,
            steps: [new SchemaStep(StepVersion, "Create the data-key table.", sql)],
            objects: [SchemaObject.Table(options.TableName)]
        );
    }
}
