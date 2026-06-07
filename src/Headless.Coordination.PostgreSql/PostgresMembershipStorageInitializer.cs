// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Coordination.PostgreSql;

#pragma warning disable CA2100 // SQL text is built from internal schema constants only.
internal sealed partial class PostgresMembershipStorageInitializer(
    IOptions<PostgreSqlCoordinationOptions> providerOptions,
    IOptions<CoordinationOptions> coordinationOptions,
    ILogger<PostgresMembershipStorageInitializer> logger
) : HostedInitializer, IMembershipStorageInitializer
{
    protected override bool RunOnStartup => providerOptions.Value.InitializeOnStartup;

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = providerOptions.Value.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = _GetCommandTimeoutSeconds(providerOptions.Value.CommandTimeout);
            command.CommandText = _CreateSchemaScript();
            command.Parameters.AddWithValue("LockResource", $"headless_coordination_init:{coordinationOptions.Value.ClusterName}");

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState is "42P06" or "42P07" or "42710" or "23505")
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            LogSchemaRaceObserved(logger, ex.SqlState, ex.MessageText);
        }
    }

    private static string _CreateSchemaScript()
    {
        return $$"""
            SELECT pg_advisory_xact_lock(hashtextextended(@LockResource, 0));

            CREATE TABLE IF NOT EXISTS {{PostgresMembershipSchema.Generation.Table}} (
                {{PostgresMembershipSchema.ClusterName}} varchar(200) NOT NULL,
                {{PostgresMembershipSchema.NodeId}} varchar(400) NOT NULL,
                {{PostgresMembershipSchema.Generation.CurrentIncarnation}} bigint NOT NULL,
                {{PostgresMembershipSchema.DateUpdated}} timestamptz NOT NULL,
                CONSTRAINT pk_{{PostgresMembershipSchema.Generation.Table}} PRIMARY KEY (
                    {{PostgresMembershipSchema.ClusterName}},
                    {{PostgresMembershipSchema.NodeId}}
                )
            );

            CREATE TABLE IF NOT EXISTS {{PostgresMembershipSchema.Descriptor.Table}} (
                {{PostgresMembershipSchema.ClusterName}} varchar(200) NOT NULL,
                {{PostgresMembershipSchema.NodeId}} varchar(400) NOT NULL,
                {{PostgresMembershipSchema.Incarnation}} bigint NOT NULL,
                {{PostgresMembershipSchema.Descriptor.HostName}} text NULL,
                {{PostgresMembershipSchema.Descriptor.Endpoints}} jsonb NOT NULL DEFAULT '{}'::jsonb,
                {{PostgresMembershipSchema.Descriptor.Role}} varchar(200) NULL,
                {{PostgresMembershipSchema.Descriptor.Metadata}} jsonb NOT NULL DEFAULT '{}'::jsonb,
                {{PostgresMembershipSchema.DateCreated}} timestamptz NOT NULL,
                CONSTRAINT pk_{{PostgresMembershipSchema.Descriptor.Table}} PRIMARY KEY (
                    {{PostgresMembershipSchema.ClusterName}},
                    {{PostgresMembershipSchema.NodeId}},
                    {{PostgresMembershipSchema.Incarnation}}
                )
            );

            CREATE TABLE IF NOT EXISTS {{PostgresMembershipSchema.Liveness.Table}} (
                {{PostgresMembershipSchema.ClusterName}} varchar(200) NOT NULL,
                {{PostgresMembershipSchema.NodeId}} varchar(400) NOT NULL,
                {{PostgresMembershipSchema.Incarnation}} bigint NOT NULL,
                {{PostgresMembershipSchema.Liveness.LastBeat}} timestamptz NOT NULL,
                {{PostgresMembershipSchema.Liveness.LeftAt}} timestamptz NULL,
                CONSTRAINT pk_{{PostgresMembershipSchema.Liveness.Table}} PRIMARY KEY (
                    {{PostgresMembershipSchema.ClusterName}},
                    {{PostgresMembershipSchema.NodeId}},
                    {{PostgresMembershipSchema.Incarnation}}
                )
            );

            CREATE INDEX IF NOT EXISTS ix_{{PostgresMembershipSchema.Liveness.Table}}_cluster_lastbeat
                ON {{PostgresMembershipSchema.Liveness.Table}} ({{PostgresMembershipSchema.ClusterName}}, {{PostgresMembershipSchema.Liveness.LastBeat}});
            """;
    }

    private static int _GetCommandTimeoutSeconds(TimeSpan timeout)
    {
        return timeout.TotalSeconds >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "PostgresCoordinationSchemaRaceObserved",
        Level = LogLevel.Information,
        Message = "Postgres coordination initializer absorbed a concurrent-DDL race (SqlState={SqlState}): {Detail}. Treating schema as initialized."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogSchemaRaceObserved(ILogger logger, string sqlState, string detail);
}
#pragma warning restore CA2100
