// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Schema;
using Headless.Sql.PostgreSql;

namespace Headless.Encryption.PostgreSql;

/// <summary>
/// The Encryption feature's schema contribution for PostgreSQL: the data-key and scopes tables, as one step.
/// </summary>
internal static class PostgreSqlEncryptionSchemaContribution
{
    public const string StepVersion = "1";

    public static SchemaContribution Create(PostgreSqlEncryptionKeyStoreOptions options)
    {
        var dialect = PostgreSqlDialect.Instance;
        var table = dialect.Qualify(options.Schema, options.TableName);
        var scopes = dialect.Qualify(options.Schema, options.ScopesTableName);
        var keysPrimaryKey = dialect.Quote("pk_" + options.TableName);
        var keysVersionKey = dialect.Quote("uq_" + options.TableName + "_version");
        var scopesPrimaryKey = dialect.Quote("pk_" + options.ScopesTableName);
        var c = new DataKeyColumns(dialect);

        // Scopes compare with the "C" collation, so tenant ids match ordinally (byte for byte) whatever the database's
        // default collation is. The unique (scope, kind, version) constraint is what makes racing creators of a
        // scope's first key agree on one row; the scope row is what an insert and a shred of one scope lock first.
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
                CONSTRAINT {keysPrimaryKey} PRIMARY KEY ({c.KeyId}),
                CONSTRAINT {keysVersionKey} UNIQUE ({c.Scope}, {c.Kind}, {c.Version})
            );

            CREATE TABLE IF NOT EXISTS {scopes} (
                {c.Scope} varchar({DataKeyFieldLimits.ScopeMaxLength}) COLLATE "C" NOT NULL,
                {c.ShreddedAt} timestamptz NULL,
                {c.CreatedAt} timestamptz NOT NULL,
                CONSTRAINT {scopesPrimaryKey} PRIMARY KEY ({c.Scope})
            );
            """;

        return new SchemaContribution(
            feature: SchemaContribution.FeatureId(
                "Encryption",
                (options.TableName, PostgreSqlEncryptionKeyStoreOptions.DefaultTableName),
                (options.ScopesTableName, PostgreSqlEncryptionKeyStoreOptions.DefaultScopesTableName)
            ),
            dialect: PostgreSqlSchemaDialect.Instance,
            createConnection: () => dialect.CreateConnection(options.ConnectionString),
            schema: options.Schema,
            steps: [new SchemaStep(StepVersion, "Create the data-key and scopes tables.", sql)],
            objects: [SchemaObject.Table(options.TableName), SchemaObject.Table(options.ScopesTableName)]
        );
    }
}
