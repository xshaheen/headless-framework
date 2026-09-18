// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;
using Headless.Hosting.Initialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Headless.Coordination.PostgreSql;

#pragma warning disable CA2100 // SQL text is built from internal schema constants only.
internal sealed partial class PostgreSqlMembershipStorageInitializer(
    IOptions<PostgreSqlCoordinationOptions> providerOptions,
    IOptions<CoordinationStorageOptions> storageOptions,
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
                command.CommandTimeout = DatabaseAdoHelpers.GetCommandTimeoutSeconds(
                    providerOptions.Value.CommandTimeout
                );
                command.CommandText = _CreateSchemaScript(storageOptions.Value.Schema);
                // Keyed on the schema, not the cluster: the DDL below creates schema-wide objects, so two
                // clusters sharing one schema must serialize on the same advisory lock.
                command.Parameters.AddWithValue(
                    "LockResource",
                    $"headless_coordination_init:{storageOptions.Value.Schema}"
                );

                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException ex)
                when (ex.SqlState
                        is SqlErrorCodes.PostgreSql.DuplicateSchema
                            or SqlErrorCodes.PostgreSql.DuplicateTable
                            or SqlErrorCodes.PostgreSql.DuplicateObject
                            or SqlErrorCodes.PostgreSql.UniqueViolation
                )
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

    private static string _CreateSchemaScript(string schema)
    {
        var generationTable = PostgreSqlMembershipSchema.Qualified(schema, PostgreSqlMembershipSchema.Generation.Table);
        var descriptorTable = PostgreSqlMembershipSchema.Qualified(schema, PostgreSqlMembershipSchema.Descriptor.Table);
        var livenessTable = PostgreSqlMembershipSchema.Qualified(schema, PostgreSqlMembershipSchema.Liveness.Table);

        return $$"""
            SELECT pg_advisory_xact_lock(hashtextextended(@LockResource, 0));

            CREATE SCHEMA IF NOT EXISTS "{{schema}}";

            CREATE TABLE IF NOT EXISTS {{generationTable}} (
                {{PostgreSqlMembershipSchema.ClusterName}} varchar(200) NOT NULL,
                {{PostgreSqlMembershipSchema.NodeId}} varchar(400) NOT NULL,
                {{PostgreSqlMembershipSchema.Generation.CurrentIncarnation}} bigint NOT NULL,
                {{PostgreSqlMembershipSchema.UpdatedAt}} timestamptz NOT NULL,
                CONSTRAINT pk_{{PostgreSqlMembershipSchema.Generation.Table}} PRIMARY KEY (
                    {{PostgreSqlMembershipSchema.ClusterName}},
                    {{PostgreSqlMembershipSchema.NodeId}}
                )
            );

            CREATE TABLE IF NOT EXISTS {{descriptorTable}} (
                {{PostgreSqlMembershipSchema.ClusterName}} varchar(200) NOT NULL,
                {{PostgreSqlMembershipSchema.NodeId}} varchar(400) NOT NULL,
                {{PostgreSqlMembershipSchema.Incarnation}} bigint NOT NULL,
                {{PostgreSqlMembershipSchema.Descriptor.HostName}} text NULL,
                {{PostgreSqlMembershipSchema.Descriptor.Endpoints}} jsonb NOT NULL DEFAULT '{}'::jsonb,
                {{PostgreSqlMembershipSchema.Descriptor.Role}} varchar(200) NULL,
                {{PostgreSqlMembershipSchema.Descriptor.Metadata}} jsonb NOT NULL DEFAULT '{}'::jsonb,
                {{PostgreSqlMembershipSchema.CreatedAt}} timestamptz NOT NULL,
                CONSTRAINT pk_{{PostgreSqlMembershipSchema.Descriptor.Table}} PRIMARY KEY (
                    {{PostgreSqlMembershipSchema.ClusterName}},
                    {{PostgreSqlMembershipSchema.NodeId}},
                    {{PostgreSqlMembershipSchema.Incarnation}}
                )
            );

            CREATE TABLE IF NOT EXISTS {{livenessTable}} (
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

            DO $migration$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM pg_attribute
                    WHERE attrelid = to_regclass('{{generationTable}}')
                      AND attname = 'date_updated'
                      AND NOT attisdropped
                ) AND NOT EXISTS (
                    SELECT 1 FROM pg_attribute
                    WHERE attrelid = to_regclass('{{generationTable}}')
                      AND attname = '{{PostgreSqlMembershipSchema.UpdatedAt}}'
                      AND NOT attisdropped
                ) THEN
                    ALTER TABLE {{generationTable}}
                        RENAME COLUMN date_updated TO {{PostgreSqlMembershipSchema.UpdatedAt}};
                END IF;

                IF EXISTS (
                    SELECT 1 FROM pg_attribute
                    WHERE attrelid = to_regclass('{{descriptorTable}}')
                      AND attname = 'date_created'
                      AND NOT attisdropped
                ) AND NOT EXISTS (
                    SELECT 1 FROM pg_attribute
                    WHERE attrelid = to_regclass('{{descriptorTable}}')
                      AND attname = '{{PostgreSqlMembershipSchema.CreatedAt}}'
                      AND NOT attisdropped
                ) THEN
                    ALTER TABLE {{descriptorTable}}
                        RENAME COLUMN date_created TO {{PostgreSqlMembershipSchema.CreatedAt}};
                END IF;
            END $migration$;

            CREATE INDEX IF NOT EXISTS ix_{{PostgreSqlMembershipSchema.Liveness.Table}}_cluster_lastbeat
                ON {{livenessTable}} ({{PostgreSqlMembershipSchema.ClusterName}}, {{PostgreSqlMembershipSchema.Liveness.LastBeat}});
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
