// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Hosting.Initialization.Schema;
using Headless.Sql.SqlServer;

namespace Headless.AuditLog.SqlServer;

/// <summary>
/// The AuditLog feature's schema contribution for SQL Server: the audit log table and its read indexes, as two
/// idempotent steps the Headless schema runner applies.
/// </summary>
internal static class SqlServerAuditLogSchemaContribution
{
    public const string TableStepVersion = "1";
    public const string IndexesStepVersion = "2";

    public static SchemaContribution Create(
        SqlServerAuditLogOptions providerOptions,
        AuditLogStorageOptions storageOptions
    )
    {
        var tableName = SqlServerAuditLogSchema.TableName(storageOptions);
        var table = SqlServerAuditLogSchema.Qualified(storageOptions);
        var objectName = SqlServerAuditLogSchema.ObjectName(storageOptions);
        var primaryKey = HeadlessStorageNaming.PrimaryKeyName(StorageNamingStyle.PascalCase, tableName);
        var jsonColumnType = (storageOptions.JsonColumnType ?? AuditLogJsonColumnType.NvarcharMax).ToSqlFragment();
        var createdAtColumnType = SqlServerAuditLogSchema.CreatedAtColumnType(storageOptions);

        var tableSql = $"""
            IF OBJECT_ID(N'{objectName}', N'U') IS NULL
                CREATE TABLE {table} (
                    [Id] bigint IDENTITY(1,1) NOT NULL,
                    [CreatedAt] {createdAtColumnType} NOT NULL,
                    [UserId] nvarchar({AuditLogFieldLimits.UserId}) NULL,
                    [AccountId] nvarchar({AuditLogFieldLimits.AccountId}) NULL,
                    [TenantId] nvarchar({AuditLogFieldLimits.TenantId}) NULL,
                    [IpAddress] nvarchar({AuditLogFieldLimits.IpAddress}) NULL,
                    [UserAgent] nvarchar({AuditLogFieldLimits.UserAgent}) NULL,
                    [CorrelationId] nvarchar({AuditLogFieldLimits.CorrelationId}) NULL,
                    [Action] nvarchar({AuditLogFieldLimits.Action}) NOT NULL,
                    [ChangeType] int NULL,
                    [EntityType] nvarchar({AuditLogFieldLimits.EntityType}) NULL,
                    [EntityId] nvarchar({AuditLogFieldLimits.EntityId}) NULL,
                    [OldValues] {jsonColumnType} NULL,
                    [NewValues] {jsonColumnType} NULL,
                    [ChangedFields] {jsonColumnType} NULL,
                    [Success] bit NOT NULL,
                    [ErrorCode] nvarchar({AuditLogFieldLimits.ErrorCode}) NULL,
                    CONSTRAINT [{primaryKey}] PRIMARY KEY CLUSTERED ([CreatedAt] ASC, [Id] ASC)
                );
            """;

        // Each index is gated by its own existence check, so a table created by other tooling without some of the
        // indexes still gets the missing ones. Each index ends in (CreatedAt, Id), the keyset order of read paging, so
        // a filtered page seeks straight to its continuation position instead of scanning.
        var indexesSql = string.Join(
            '\n',
            _IndexStatement(
                _IndexName(tableName, AuditLogStorageNames.TenantTime),
                table,
                objectName,
                "[TenantId] ASC, [CreatedAt] ASC, [Id] ASC"
            ),
            _IndexStatement(
                _IndexName(tableName, AuditLogStorageNames.TenantActionTime),
                table,
                objectName,
                "[TenantId] ASC, [Action] ASC, [CreatedAt] ASC, [Id] ASC"
            ),
            _IndexStatement(
                _IndexName(tableName, AuditLogStorageNames.TenantEntityTime),
                table,
                objectName,
                "[TenantId] ASC, [EntityType] ASC, [EntityId] ASC, [CreatedAt] ASC, [Id] ASC"
            ),
            _IndexStatement(
                _IndexName(tableName, AuditLogStorageNames.TenantActorTime),
                table,
                objectName,
                "[TenantId] ASC, [UserId] ASC, [CreatedAt] ASC, [Id] ASC"
            ),
            _IndexStatement(
                _IndexName(tableName, AuditLogStorageNames.TenantAccountTime),
                table,
                objectName,
                "[TenantId] ASC, [AccountId] ASC, [CreatedAt] ASC, [Id] ASC"
            ),
            _IndexStatement(
                _IndexName(tableName, AuditLogStorageNames.Correlation),
                table,
                objectName,
                "[CorrelationId] ASC, [CreatedAt] ASC, [Id] ASC"
            )
        );

        return new SchemaContribution(
            feature: SchemaContribution.FeatureId(
                "AuditLog",
                (
                    tableName,
                    HeadlessStorageNaming.Apply(StorageNamingStyle.PascalCase, AuditLogStorageNames.DefaultTableName)
                )
            ),
            dialect: SqlServerSchemaDialect.Instance,
            createConnection: providerOptions.CreateConnection,
            schema: storageOptions.Schema,
            steps:
            [
                new SchemaStep(TableStepVersion, "Create the audit log table.", tableSql),
                new SchemaStep(IndexesStepVersion, "Create the audit log read indexes.", indexesSql),
            ],
            applyOnStartup: storageOptions.InitializeOnStartup
        );
    }

    private static string _IndexName(string tableName, string[] parts)
    {
        return HeadlessStorageNaming.IndexName(StorageNamingStyle.PascalCase, tableName, parts);
    }

    private static string _IndexStatement(string indexName, string table, string objectName, string columns)
    {
        return $"""
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{indexName}' AND object_id = OBJECT_ID(N'{objectName}'))
                CREATE NONCLUSTERED INDEX [{indexName}] ON {table} ({columns});
            """;
    }
}
