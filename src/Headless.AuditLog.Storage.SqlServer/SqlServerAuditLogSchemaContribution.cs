// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Hosting.Initialization;
using Headless.Hosting.Initialization.Schema;
using Headless.Sql.SqlServer;
using Microsoft.Data.SqlClient;

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
        RelationalAuditLogTable auditLogTable,
        AuditLogStorageOptions storageOptions
    )
    {
        var tableName = auditLogTable.Name;
        var table = auditLogTable.Qualified;
        var objectName = $"{auditLogTable.Schema}.{tableName}";
        var primaryKey = HeadlessStorageNaming.PrimaryKeyName(StorageNamingStyle.PascalCase, tableName);
        var jsonColumnType = auditLogTable.JsonColumnType;
        var createdAtColumnType = _CreatedAtColumnType(storageOptions);

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
            createConnection: () => SqlServerDialect.Instance.CreateConnection(providerOptions.ConnectionString),
            schema: auditLogTable.Schema,
            steps:
            [
                new SchemaStep(TableStepVersion, "Create the audit log table.", tableSql),
                new SchemaStep(IndexesStepVersion, "Create the audit log read indexes.", indexesSql),
            ],
            applyOnStartup: storageOptions.InitializeOnStartup
        );
    }

    /// <summary>
    /// Returns the binder for the configured <c>CreatedAt</c> column type. An untyped <c>DateTime</c> would bind as
    /// legacy <c>datetime</c> and round to about 3 ms, collapsing distinct timestamps that read paging orders by, and a
    /// <c>datetimeoffset</c> parameter against a <c>datetime2</c> column would convert the column and lose its index.
    /// </summary>
    public static AuditLogCreatedAtBinder CreatedAtBinder(AuditLogStorageOptions storageOptions)
    {
        var isOffset = _CreatedAtColumnType(storageOptions)
            .StartsWith("datetimeoffset", StringComparison.OrdinalIgnoreCase);

        return (command, parameter, value) =>
        {
            var utc = value.ToUniversalTime();
            command.Parameters.Add(
                isOffset
                    ? new SqlParameter(parameter, SqlDbType.DateTimeOffset) { Value = utc }
                    : new SqlParameter(parameter, SqlDbType.DateTime2) { Value = utc.UtcDateTime }
            );
        };
    }

    /// <summary>The <c>CreatedAt</c> column's type: the configured override, else <c>datetimeoffset(7)</c>.</summary>
    private static string _CreatedAtColumnType(AuditLogStorageOptions storageOptions)
    {
        return string.IsNullOrWhiteSpace(storageOptions.CreatedAtColumnType)
            ? "datetimeoffset(7)"
            : storageOptions.CreatedAtColumnType;
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
