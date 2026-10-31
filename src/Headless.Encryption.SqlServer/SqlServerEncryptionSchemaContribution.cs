// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Schema;
using Headless.Sql.SqlServer;

namespace Headless.Encryption.SqlServer;

/// <summary>
/// The Encryption feature's schema contribution for SQL Server: the data-key and scopes tables, as one step.
/// </summary>
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
        var scopes = dialect.Qualify(options.Schema, options.ScopesTableName);
        var scopesObjectName = $"{options.Schema}.{options.ScopesTableName}";
        var keysPrimaryKey = dialect.Quote("PK_" + options.TableName);
        var keysVersionKey = dialect.Quote("UQ_" + options.TableName + "_Version");
        var scopesPrimaryKey = dialect.Quote("PK_" + options.ScopesTableName);
        var c = new DataKeyColumns(dialect);

        // The clustered unique (Scope, Kind, Version) index is load-bearing: the insert-if-absent's HOLDLOCK takes its
        // key-range lock on it, which is what serializes racing creators of a scope's first key. The id key is
        // nonclustered because version-7 UUIDs do not sort by time under uniqueidentifier ordering. The scope row is
        // what an insert and a shred of one scope lock first.
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
                    CONSTRAINT {keysPrimaryKey} PRIMARY KEY NONCLUSTERED ({c.KeyId}),
                    CONSTRAINT {keysVersionKey} UNIQUE CLUSTERED ({c.Scope} ASC, {c.Kind} ASC, {c.Version} ASC)
                );

            IF OBJECT_ID(N'{scopesObjectName}', N'U') IS NULL
                CREATE TABLE {scopes} (
                    {c.Scope} nvarchar({DataKeyFieldLimits.ScopeMaxLength}) COLLATE {_KeyCollation} NOT NULL,
                    {c.ShreddedAt} datetimeoffset(7) NULL,
                    {c.CreatedAt} datetimeoffset(7) NOT NULL,
                    CONSTRAINT {scopesPrimaryKey} PRIMARY KEY CLUSTERED ({c.Scope} ASC)
                );
            """;

        return new SchemaContribution(
            feature: SchemaContribution.FeatureId(
                "Encryption",
                (options.TableName, SqlServerEncryptionKeyStoreOptions.DefaultTableName),
                (options.ScopesTableName, SqlServerEncryptionKeyStoreOptions.DefaultScopesTableName)
            ),
            dialect: SqlServerSchemaDialect.Instance,
            createConnection: () => dialect.CreateConnection(options.ConnectionString),
            schema: options.Schema,
            steps: [new SchemaStep(StepVersion, "Create the data-key and scopes tables.", sql)],
            objects: [SchemaObject.Table(options.TableName), SchemaObject.Table(options.ScopesTableName)]
        );
    }
}
