// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Schema;
using Headless.Sql.SqlServer;

namespace Headless.Encryption.SqlServer;

/// <summary>The Encryption feature's schema contribution for SQL Server: the data-key table, as one step.</summary>
internal static class SqlServerEncryptionSchemaContribution
{
    public const string StepVersion = "1";

    // Binary code-point order, so scopes match case- and accent-sensitively whatever the database's default collation
    // is. Scopes with surrounding white space are refused before any statement, which keeps matching ordinal.
    private const string _KeyCollation = "Latin1_General_100_BIN2";

    public static SchemaContribution Create(SqlServerEncryptionKeyStoreOptions options)
    {
        var dialect = SqlServerDialect.Instance;
        var table = dialect.Qualify(options.Schema, options.TableName);
        var objectName = $"{options.Schema}.{options.TableName}";
        var c = new DataKeyColumns(dialect);

        // The clustered unique (Scope, Kind, Version) index is load-bearing: the insert-if-absent's HOLDLOCK takes its
        // key-range lock on it, which is what serializes racing creators of a scope's first key. The id key is
        // nonclustered because version-7 UUIDs do not sort by time under uniqueidentifier ordering.
        var sql = $"""
            IF OBJECT_ID(N'{objectName}', N'U') IS NULL
                CREATE TABLE {table} (
                    {c.KeyId} uniqueidentifier NOT NULL,
                    {c.Scope} nvarchar({DataKeyFieldLimits.ScopeMaxLength}) COLLATE {_KeyCollation} NOT NULL,
                    {c.Kind} smallint NOT NULL,
                    {c.Version} int NOT NULL,
                    {c.KekProvider} nvarchar({DataKeyFieldLimits.KekProviderMaxLength}) NOT NULL,
                    {c.KekId} nvarchar({DataKeyFieldLimits.KekIdMaxLength}) NOT NULL,
                    {c.WrappedKey} varbinary(max) NOT NULL,
                    {c.CreatedAt} datetimeoffset(7) NOT NULL,
                    CONSTRAINT {dialect.Quote("PK_" + options.TableName)} PRIMARY KEY NONCLUSTERED ({c.KeyId}),
                    CONSTRAINT {dialect.Quote(
                "UQ_" + options.TableName + "_Version"
            )} UNIQUE CLUSTERED ({c.Scope} ASC, {c.Kind} ASC, {c.Version} ASC)
                );
            """;

        return new SchemaContribution(
            feature: SchemaContribution.FeatureId(
                "Encryption",
                (options.TableName, SqlServerEncryptionKeyStoreOptions.DefaultTableName)
            ),
            dialect: SqlServerSchemaDialect.Instance,
            createConnection: () => dialect.CreateConnection(options.ConnectionString),
            schema: options.Schema,
            steps: [new SchemaStep(StepVersion, "Create the data-key table.", sql)],
            objects: [SchemaObject.Table(options.TableName)]
        );
    }
}
