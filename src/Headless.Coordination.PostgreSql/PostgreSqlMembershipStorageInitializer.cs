// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Coordination.PostgreSql;

#pragma warning disable CA2100 // SQL text is built from internal schema constants only.
internal sealed partial class PostgreSqlMembershipStorageInitializer(
    IOptions<PostgreSqlCoordinationOptions> providerOptions,
    IOptions<CoordinationOptions> coordinationOptions,
    ILogger<PostgreSqlMembershipStorageInitializer> logger
) : HostedInitializer, IMembershipStorageInitializer
{
    protected override bool RunOnStartup => providerOptions.Value.InitializeOnStartup;

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = providerOptions.Value.CreateConnection();

        try
        {
            // Open and BeginTransaction live inside the outer try so connection-setup failures (bad connection
            // string, unreachable host) also surface through the diagnostic InvalidOperationException wrap below
            // rather than as a raw NpgsqlException. The inner try absorbs the concurrent-DDL race; any other failure
            // bubbles out, disposing (and thus rolling back) the transaction on the way to the wrap.
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandTimeout = DatabaseAdoHelpers.GetCommandTimeoutSeconds(providerOptions.Value.CommandTimeout);
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "Headless.Coordination.PostgreSql: failed to initialize the membership schema.",
                ex
            );
        }
    }

    private static string _CreateSchemaScript()
    {
        return $$"""
            SELECT pg_advisory_xact_lock(hashtextextended(@LockResource, 0));

            CREATE TABLE IF NOT EXISTS {{PostgreSqlMembershipSchema.Generation.Table}} (
                {{PostgreSqlMembershipSchema.ClusterName}} varchar(200) NOT NULL,
                {{PostgreSqlMembershipSchema.NodeId}} varchar(400) NOT NULL,
                {{PostgreSqlMembershipSchema.Generation.CurrentIncarnation}} bigint NOT NULL,
                {{PostgreSqlMembershipSchema.DateUpdated}} timestamptz NOT NULL,
                CONSTRAINT pk_{{PostgreSqlMembershipSchema.Generation.Table}} PRIMARY KEY (
                    {{PostgreSqlMembershipSchema.ClusterName}},
                    {{PostgreSqlMembershipSchema.NodeId}}
                )
            );

            CREATE TABLE IF NOT EXISTS {{PostgreSqlMembershipSchema.Descriptor.Table}} (
                {{PostgreSqlMembershipSchema.ClusterName}} varchar(200) NOT NULL,
                {{PostgreSqlMembershipSchema.NodeId}} varchar(400) NOT NULL,
                {{PostgreSqlMembershipSchema.Incarnation}} bigint NOT NULL,
                {{PostgreSqlMembershipSchema.Descriptor.HostName}} text NULL,
                {{PostgreSqlMembershipSchema.Descriptor.Endpoints}} jsonb NOT NULL DEFAULT '{}'::jsonb,
                {{PostgreSqlMembershipSchema.Descriptor.Role}} varchar(200) NULL,
                {{PostgreSqlMembershipSchema.Descriptor.Metadata}} jsonb NOT NULL DEFAULT '{}'::jsonb,
                {{PostgreSqlMembershipSchema.DateCreated}} timestamptz NOT NULL,
                CONSTRAINT pk_{{PostgreSqlMembershipSchema.Descriptor.Table}} PRIMARY KEY (
                    {{PostgreSqlMembershipSchema.ClusterName}},
                    {{PostgreSqlMembershipSchema.NodeId}},
                    {{PostgreSqlMembershipSchema.Incarnation}}
                )
            );

            CREATE TABLE IF NOT EXISTS {{PostgreSqlMembershipSchema.Liveness.Table}} (
                {{PostgreSqlMembershipSchema.ClusterName}} varchar(200) NOT NULL,
                {{PostgreSqlMembershipSchema.NodeId}} varchar(400) NOT NULL,
                {{PostgreSqlMembershipSchema.Incarnation}} bigint NOT NULL,
                {{PostgreSqlMembershipSchema.Liveness.LastBeat}} timestamptz NOT NULL,
                {{PostgreSqlMembershipSchema.Liveness.LeftAt}} timestamptz NULL,
                CONSTRAINT pk_{{PostgreSqlMembershipSchema.Liveness.Table}} PRIMARY KEY (
                    {{PostgreSqlMembershipSchema.ClusterName}},
                    {{PostgreSqlMembershipSchema.NodeId}},
                    {{PostgreSqlMembershipSchema.Incarnation}}
                )
            );

            CREATE INDEX IF NOT EXISTS ix_{{PostgreSqlMembershipSchema.Liveness.Table}}_cluster_lastbeat
                ON {{PostgreSqlMembershipSchema.Liveness.Table}} ({{PostgreSqlMembershipSchema.ClusterName}}, {{PostgreSqlMembershipSchema.Liveness.LastBeat}});
            """;
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
