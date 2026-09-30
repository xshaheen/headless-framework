// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Hosting.Initialization.Schema;
using Headless.Sql.SqlServer;

namespace Headless.Coordination.SqlServer;

/// <summary>
/// The Coordination feature's schema contribution for SQL Server: the membership generation, descriptor, and
/// liveness tables plus the liveness index, and the legacy column renames, as two idempotent steps the Headless
/// schema runner applies.
/// </summary>
internal static class SqlServerMembershipSchemaContribution
{
    public const string TablesStepVersion = "1";
    public const string RenameStepVersion = "2";

    public static SchemaContribution Create(
        SqlServerCoordinationOptions providerOptions,
        CoordinationStorageOptions storageOptions
    )
    {
        var schema = storageOptions.Schema;
        var generationTable = _Qualified(schema, SqlServerMembershipSchema.Generation.Table);
        var descriptorTable = _Qualified(schema, SqlServerMembershipSchema.Descriptor.Table);
        var livenessTable = _Qualified(schema, SqlServerMembershipSchema.Liveness.Table);
        var generationObject = SqlServerCoordinationIdentifier.ObjectName(
            schema,
            SqlServerMembershipSchema.Generation.Table
        );
        var descriptorObject = SqlServerCoordinationIdentifier.ObjectName(
            schema,
            SqlServerMembershipSchema.Descriptor.Table
        );
        var livenessObject = SqlServerCoordinationIdentifier.ObjectName(
            schema,
            SqlServerMembershipSchema.Liveness.Table
        );

        var tablesSql = $$"""
            IF OBJECT_ID(N'{{generationObject}}', N'U') IS NULL
                CREATE TABLE {{generationTable}} (
                    [{{SqlServerMembershipSchema.ClusterName}}] nvarchar(200) NOT NULL,
                    [{{SqlServerMembershipSchema.NodeId}}] nvarchar(400) NOT NULL,
                    [{{SqlServerMembershipSchema.Generation.CurrentIncarnation}}] bigint NOT NULL,
                    [{{SqlServerMembershipSchema.UpdatedAt}}] datetime2(7) NOT NULL,
                    CONSTRAINT [PK_{{SqlServerMembershipSchema.Generation.Table}}] PRIMARY KEY CLUSTERED (
                        [{{SqlServerMembershipSchema.ClusterName}}] ASC,
                        [{{SqlServerMembershipSchema.NodeId}}] ASC
                    )
                );

            IF OBJECT_ID(N'{{descriptorObject}}', N'U') IS NULL
                CREATE TABLE {{descriptorTable}} (
                    [{{SqlServerMembershipSchema.ClusterName}}] nvarchar(200) NOT NULL,
                    [{{SqlServerMembershipSchema.NodeId}}] nvarchar(400) NOT NULL,
                    [{{SqlServerMembershipSchema.Incarnation}}] bigint NOT NULL,
                    [{{SqlServerMembershipSchema.Descriptor.HostName}}] nvarchar(max) NULL,
                    [{{SqlServerMembershipSchema.Descriptor.Endpoints}}] nvarchar(max) NOT NULL CONSTRAINT [DF_{{SqlServerMembershipSchema.Descriptor.Table}}_Endpoints] DEFAULT N'{}',
                    [{{SqlServerMembershipSchema.Descriptor.Role}}] nvarchar(200) NULL,
                    [{{SqlServerMembershipSchema.Descriptor.Metadata}}] nvarchar(max) NOT NULL CONSTRAINT [DF_{{SqlServerMembershipSchema.Descriptor.Table}}_Metadata] DEFAULT N'{}',
                    [{{SqlServerMembershipSchema.CreatedAt}}] datetime2(7) NOT NULL,
                    CONSTRAINT [PK_{{SqlServerMembershipSchema.Descriptor.Table}}] PRIMARY KEY CLUSTERED (
                        [{{SqlServerMembershipSchema.ClusterName}}] ASC,
                        [{{SqlServerMembershipSchema.NodeId}}] ASC,
                        [{{SqlServerMembershipSchema.Incarnation}}] ASC
                    )
                );

            IF OBJECT_ID(N'{{livenessObject}}', N'U') IS NULL
                CREATE TABLE {{livenessTable}} (
                    [{{SqlServerMembershipSchema.ClusterName}}] nvarchar(200) NOT NULL,
                    [{{SqlServerMembershipSchema.NodeId}}] nvarchar(400) NOT NULL,
                    [{{SqlServerMembershipSchema.Incarnation}}] bigint NOT NULL,
                    [{{SqlServerMembershipSchema.Liveness.LastBeat}}] datetime2(7) NOT NULL,
                    [{{SqlServerMembershipSchema.Liveness.LeftAt}}] datetime2(7) NULL,
                    CONSTRAINT [PK_{{SqlServerMembershipSchema.Liveness.Table}}] PRIMARY KEY CLUSTERED (
                        [{{SqlServerMembershipSchema.ClusterName}}] ASC,
                        [{{SqlServerMembershipSchema.NodeId}}] ASC,
                        [{{SqlServerMembershipSchema.Incarnation}}] ASC
                    )
                );

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'IX_{{SqlServerMembershipSchema.Liveness.Table}}_ClusterName_LastBeat'
                  AND object_id = OBJECT_ID(N'{{livenessObject}}')
            )
                CREATE NONCLUSTERED INDEX [IX_{{SqlServerMembershipSchema.Liveness.Table}}_ClusterName_LastBeat]
                    ON {{livenessTable}} ([{{SqlServerMembershipSchema.ClusterName}}] ASC, [{{SqlServerMembershipSchema.Liveness.LastBeat}}] ASC);
            """;

        // The renames are a separate step: they are a schema repair, not a creation, and keeping them apart keeps
        // the creation step's checksum stable while the repair evolves.
        var renameSql = $$"""
            IF COL_LENGTH(N'{{generationObject}}', N'DateUpdated') IS NOT NULL
               AND COL_LENGTH(N'{{generationObject}}', N'{{SqlServerMembershipSchema.UpdatedAt}}') IS NULL
                EXEC sys.sp_rename N'{{generationObject}}.DateUpdated', N'{{SqlServerMembershipSchema.UpdatedAt}}', N'COLUMN';

            IF COL_LENGTH(N'{{descriptorObject}}', N'DateCreated') IS NOT NULL
               AND COL_LENGTH(N'{{descriptorObject}}', N'{{SqlServerMembershipSchema.CreatedAt}}') IS NULL
                EXEC sys.sp_rename N'{{descriptorObject}}.DateCreated', N'{{SqlServerMembershipSchema.CreatedAt}}', N'COLUMN';
            """;

        return new SchemaContribution(
            feature: "Coordination",
            dialect: SqlServerSchemaDialect.Instance,
            createConnection: providerOptions.CreateConnection,
            schema: schema,
            steps:
            [
                new SchemaStep(
                    TablesStepVersion,
                    "Create the membership generation, descriptor, and liveness tables.",
                    tablesSql
                ),
                new SchemaStep(
                    RenameStepVersion,
                    "Rename the legacy DateUpdated and DateCreated columns to the current names.",
                    renameSql
                ),
            ],
            applyOnStartup: providerOptions.InitializeOnStartup
        );
    }

    private static string _Qualified(string schema, string table)
    {
        return SqlServerCoordinationIdentifier.Qualified(schema, table);
    }
}
