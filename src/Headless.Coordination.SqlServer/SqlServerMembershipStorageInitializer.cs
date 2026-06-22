// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Microsoft.Extensions.Options;

namespace Headless.Coordination.SqlServer;

#pragma warning disable CA2100 // SQL text is built from validated schema plus internal table constants.
internal sealed class SqlServerMembershipStorageInitializer(IOptions<SqlServerCoordinationOptions> providerOptions)
    : HostedInitializer,
        IMembershipStorageInitializer
{
    protected override bool RunOnStartup => providerOptions.Value.InitializeOnStartup;

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandTimeout = DatabaseAdoHelpers.GetCommandTimeoutSeconds(providerOptions.Value.CommandTimeout);
        command.CommandText = _CreateScript(providerOptions.Value);
        command.Parameters.AddWithValue(
            "LockTimeout",
            _GetLockTimeoutMilliseconds(providerOptions.Value.CommandTimeout)
        );
        command.Parameters.AddWithValue("LockResource", $"headless_coordination_init:{providerOptions.Value.Schema}");

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string _CreateScript(SqlServerCoordinationOptions provider)
    {
        var schema = provider.Schema;
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

        return $$"""
            DECLARE @lockResult int;
            EXEC @lockResult = sys.sp_getapplock
                @Resource = @LockResource,
                @LockMode = N'Exclusive',
                @LockOwner = N'Session',
                @LockTimeout = @LockTimeout,
                @DbPrincipal = N'public';

            IF @lockResult < 0
                THROW 50000, N'Headless.Coordination.SqlServer: failed to acquire membership schema initialization lock.', 1;

            BEGIN TRY
                BEGIN TRY
                    IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'{{schema}}')
                        EXEC(N'CREATE SCHEMA {{SqlServerCoordinationIdentifier.Quote(schema)}}');
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
                END CATCH;

                BEGIN TRY
                    IF OBJECT_ID(N'{{generationObject}}', N'U') IS NULL
                    BEGIN
                        CREATE TABLE {{generationTable}} (
                            [{{SqlServerMembershipSchema.ClusterName}}] nvarchar(200) NOT NULL,
                            [{{SqlServerMembershipSchema.NodeId}}] nvarchar(400) NOT NULL,
                            [{{SqlServerMembershipSchema.Generation.CurrentIncarnation}}] bigint NOT NULL,
                            [{{SqlServerMembershipSchema.DateUpdated}}] datetime2(7) NOT NULL,
                            CONSTRAINT [PK_{{SqlServerMembershipSchema.Generation.Table}}] PRIMARY KEY CLUSTERED (
                                [{{SqlServerMembershipSchema.ClusterName}}] ASC,
                                [{{SqlServerMembershipSchema.NodeId}}] ASC
                            )
                        );
                    END;
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
                END CATCH;

                BEGIN TRY
                    IF OBJECT_ID(N'{{descriptorObject}}', N'U') IS NULL
                    BEGIN
                        CREATE TABLE {{descriptorTable}} (
                            [{{SqlServerMembershipSchema.ClusterName}}] nvarchar(200) NOT NULL,
                            [{{SqlServerMembershipSchema.NodeId}}] nvarchar(400) NOT NULL,
                            [{{SqlServerMembershipSchema.Incarnation}}] bigint NOT NULL,
                            [{{SqlServerMembershipSchema.Descriptor.HostName}}] nvarchar(max) NULL,
                            [{{SqlServerMembershipSchema.Descriptor.Endpoints}}] nvarchar(max) NOT NULL CONSTRAINT [DF_{{SqlServerMembershipSchema.Descriptor.Table}}_Endpoints] DEFAULT N'{}',
                            [{{SqlServerMembershipSchema.Descriptor.Role}}] nvarchar(200) NULL,
                            [{{SqlServerMembershipSchema.Descriptor.Metadata}}] nvarchar(max) NOT NULL CONSTRAINT [DF_{{SqlServerMembershipSchema.Descriptor.Table}}_Metadata] DEFAULT N'{}',
                            [{{SqlServerMembershipSchema.DateCreated}}] datetime2(7) NOT NULL,
                            CONSTRAINT [PK_{{SqlServerMembershipSchema.Descriptor.Table}}] PRIMARY KEY CLUSTERED (
                                [{{SqlServerMembershipSchema.ClusterName}}] ASC,
                                [{{SqlServerMembershipSchema.NodeId}}] ASC,
                                [{{SqlServerMembershipSchema.Incarnation}}] ASC
                            )
                        );
                    END;
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
                END CATCH;

                BEGIN TRY
                    IF OBJECT_ID(N'{{livenessObject}}', N'U') IS NULL
                    BEGIN
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
                    END;
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
                END CATCH;

                BEGIN TRY
                    IF NOT EXISTS (
                        SELECT 1
                        FROM sys.indexes
                        WHERE name = N'IX_{{SqlServerMembershipSchema.Liveness.Table}}_ClusterName_LastBeat'
                          AND object_id = OBJECT_ID(N'{{livenessObject}}')
                    )
                        CREATE NONCLUSTERED INDEX [IX_{{SqlServerMembershipSchema.Liveness.Table}}_ClusterName_LastBeat]
                            ON {{livenessTable}} ([{{SqlServerMembershipSchema.ClusterName}}] ASC, [{{SqlServerMembershipSchema.Liveness.LastBeat}}] ASC);
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
                END CATCH;

                EXEC sys.sp_releaseapplock @Resource = @LockResource, @LockOwner = N'Session', @DbPrincipal = N'public';
            END TRY
            BEGIN CATCH
                BEGIN TRY
                    IF APPLOCK_MODE(N'public', @LockResource, N'Session') <> N'NoLock'
                        EXEC sys.sp_releaseapplock @Resource = @LockResource, @LockOwner = N'Session', @DbPrincipal = N'public';
                END TRY
                BEGIN CATCH
                END CATCH;

                THROW;
            END CATCH;
            """;
    }

    private static string _Qualified(string schema, string table)
    {
        return SqlServerCoordinationIdentifier.Qualified(schema, table);
    }

    private static int _GetLockTimeoutMilliseconds(TimeSpan timeout)
    {
        return timeout.TotalMilliseconds >= int.MaxValue
            ? int.MaxValue
            : Math.Max(1, (int)Math.Ceiling(timeout.TotalMilliseconds));
    }
}
#pragma warning restore CA2100
