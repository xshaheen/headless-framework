// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Headless.Coordination.SqlServer;

#pragma warning disable CA2100 // SQL text is built from validated schema plus internal table constants.
internal sealed class SqlServerMembershipStorageInitializer(
    IOptions<SqlServerCoordinationOptions> providerOptions,
    IOptions<CoordinationOptions> coordinationOptions
) : HostedInitializer, IMembershipStorageInitializer
{
    protected override bool RunOnStartup => providerOptions.Value.InitializeOnStartup;

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandTimeout = _GetCommandTimeoutSeconds(providerOptions.Value.CommandTimeout);
        command.CommandText = _CreateScript(providerOptions.Value, coordinationOptions.Value);
        command.Parameters.AddWithValue("LockTimeout", _GetLockTimeoutMilliseconds(providerOptions.Value.CommandTimeout));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string _CreateScript(SqlServerCoordinationOptions provider, CoordinationOptions coordination)
    {
        var schema = provider.Schema;
        var generationTable = _Qualified(schema, MembershipSchema.Generation.Table);
        var descriptorTable = _Qualified(schema, MembershipSchema.Descriptor.Table);
        var livenessTable = _Qualified(schema, MembershipSchema.Liveness.Table);
        var generationObject = SqlServerCoordinationIdentifier.ObjectName(schema, MembershipSchema.Generation.Table);
        var descriptorObject = SqlServerCoordinationIdentifier.ObjectName(schema, MembershipSchema.Descriptor.Table);
        var livenessObject = SqlServerCoordinationIdentifier.ObjectName(schema, MembershipSchema.Liveness.Table);
        var lockResource = $"headless_coordination_init:{schema}:{coordination.ClusterName}";

        return $$"""
            DECLARE @lockResult int;
            EXEC @lockResult = sys.sp_getapplock
                @Resource = N'{{lockResource}}',
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
                            [{{MembershipSchema.ClusterName}}] nvarchar(200) NOT NULL,
                            [{{MembershipSchema.NodeId}}] nvarchar(400) NOT NULL,
                            [{{MembershipSchema.Generation.CurrentIncarnation}}] bigint NOT NULL,
                            [{{MembershipSchema.UpdatedAt}}] datetime2(7) NOT NULL,
                            CONSTRAINT [pk_{{MembershipSchema.Generation.Table}}] PRIMARY KEY CLUSTERED (
                                [{{MembershipSchema.ClusterName}}] ASC,
                                [{{MembershipSchema.NodeId}}] ASC
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
                            [{{MembershipSchema.ClusterName}}] nvarchar(200) NOT NULL,
                            [{{MembershipSchema.NodeId}}] nvarchar(400) NOT NULL,
                            [{{MembershipSchema.Incarnation}}] bigint NOT NULL,
                            [{{MembershipSchema.Descriptor.HostName}}] nvarchar(max) NULL,
                            [{{MembershipSchema.Descriptor.Endpoints}}] nvarchar(max) NOT NULL CONSTRAINT [df_{{MembershipSchema.Descriptor.Table}}_endpoints] DEFAULT N'{}',
                            [{{MembershipSchema.Descriptor.Role}}] nvarchar(200) NULL,
                            [{{MembershipSchema.Descriptor.Metadata}}] nvarchar(max) NOT NULL CONSTRAINT [df_{{MembershipSchema.Descriptor.Table}}_metadata] DEFAULT N'{}',
                            [{{MembershipSchema.CreatedAt}}] datetime2(7) NOT NULL,
                            CONSTRAINT [pk_{{MembershipSchema.Descriptor.Table}}] PRIMARY KEY CLUSTERED (
                                [{{MembershipSchema.ClusterName}}] ASC,
                                [{{MembershipSchema.NodeId}}] ASC,
                                [{{MembershipSchema.Incarnation}}] ASC
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
                            [{{MembershipSchema.ClusterName}}] nvarchar(200) NOT NULL,
                            [{{MembershipSchema.NodeId}}] nvarchar(400) NOT NULL,
                            [{{MembershipSchema.Incarnation}}] bigint NOT NULL,
                            [{{MembershipSchema.Liveness.LastBeat}}] datetime2(7) NOT NULL,
                            [{{MembershipSchema.Liveness.LeftAt}}] datetime2(7) NULL,
                            CONSTRAINT [pk_{{MembershipSchema.Liveness.Table}}] PRIMARY KEY CLUSTERED (
                                [{{MembershipSchema.ClusterName}}] ASC,
                                [{{MembershipSchema.NodeId}}] ASC,
                                [{{MembershipSchema.Incarnation}}] ASC
                            )
                        );
                    END;
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() NOT IN (2714, 1913, 2759) THROW;
                END CATCH;

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'ix_{{MembershipSchema.Liveness.Table}}_cluster_lastbeat'
                      AND object_id = OBJECT_ID(N'{{livenessObject}}')
                )
                    CREATE NONCLUSTERED INDEX [ix_{{MembershipSchema.Liveness.Table}}_cluster_lastbeat]
                        ON {{livenessTable}} ([{{MembershipSchema.ClusterName}}] ASC, [{{MembershipSchema.Liveness.LastBeat}}] ASC);

                EXEC sys.sp_releaseapplock @Resource = N'{{lockResource}}', @LockOwner = N'Session', @DbPrincipal = N'public';
            END TRY
            BEGIN CATCH
                BEGIN TRY
                    IF APPLOCK_MODE(N'public', N'{{lockResource}}', N'Session') <> N'NoLock'
                        EXEC sys.sp_releaseapplock @Resource = N'{{lockResource}}', @LockOwner = N'Session', @DbPrincipal = N'public';
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

    private static int _GetCommandTimeoutSeconds(TimeSpan timeout)
    {
        return timeout.TotalSeconds >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));
    }

    private static int _GetLockTimeoutMilliseconds(TimeSpan timeout)
    {
        return timeout.TotalMilliseconds >= int.MaxValue
            ? int.MaxValue
            : Math.Max(1, (int)Math.Ceiling(timeout.TotalMilliseconds));
    }
}
#pragma warning restore CA2100
