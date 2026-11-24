// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Hosting;
using Headless.Hosting.Schema;
using Headless.Messaging;
using Headless.Messaging.Storage.PostgreSql;
using Headless.Sql.PostgreSql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

/// <summary>
/// A database that already ran the steps of the build before the correlation columns: its host refuses to start until
/// the new step is provisioned, and provisioning applies that step alone.
/// </summary>
[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlCorrelationUpgradeTests(PostgreSqlTestFixture fixture) : TestBase
{
    [Fact]
    public async Task should_fail_a_start_before_provisioning_and_apply_only_the_correlation_step_after()
    {
        // given: a database at the steps the previous build shipped
        var schema = $"msg_upgrade_{Guid.NewGuid():N}"[..24];
        var options = new PostgreSqlOptions { ConnectionString = fixture.ConnectionString };
        var current = TestMessagingSchema.Contribution(options, schema);
        var shipped = new SchemaContribution(
            current.Feature,
            current.Dialect,
            current.CreateConnection,
            current.Schema,
            [
                .. current.Steps.Where(static step =>
                    !string.Equals(
                        step.Version,
                        PostgreSqlMessagingSchemaContribution.CorrelationStepVersion,
                        StringComparison.Ordinal
                    )
                ),
            ],
            objects: current.Objects
        );

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.Options.Version = "v1";
            setup.ConfigureStorage(storage => storage.Schema = schema);
            setup.UsePostgreSql(fixture.ConnectionString);
        });
        await using var provider = services.BuildServiceProvider();

        try
        {
            var before = await new SchemaRunner([shipped]).ApplyAsync(AbortToken);
            before.AppliedSteps.Select(static step => step.Version).Should().Equal("1", "2", "3");
            var historyBefore = await _ReadHistoryAsync(schema);

            // when: the new build starts before provisioning
            var start = async () =>
            {
                foreach (var validator in provider.GetServices<IHeadlessStartupValidator>())
                {
                    await validator.ValidateAsync(AbortToken);
                }
            };

            // then: the start fails and names the missing step
            (await start.Should().ThrowAsync<Exception>())
                .Which.Message.Should()
                .Contain($"Steps {PostgreSqlMessagingSchemaContribution.CorrelationStepVersion} of Messaging");

            // when: provisioning runs
            var provisioned = await new SchemaRunner([current]).ApplyAsync(AbortToken);

            // then: only the new step ran, the recorded ones are untouched, and the columns and indexes exist
            provisioned
                .AppliedSteps.Should()
                .Equal(
                    new SchemaAppliedStep(
                        schema,
                        "Messaging",
                        PostgreSqlMessagingSchemaContribution.CorrelationStepVersion
                    )
                );
            (await _ReadHistoryAsync(schema)).Take(historyBefore.Count).Should().Equal(historyBefore);
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync(AbortToken);
            var objects = await connection.QueryAsync<string>(
                """
                SELECT table_name || '.' || column_name FROM information_schema.columns
                WHERE table_schema=@Schema AND column_name IN ('correlation_id','causation_id')
                UNION ALL
                SELECT indexname FROM pg_indexes WHERE schemaname=@Schema AND indexname LIKE 'idx_messaging_%_correlation';
                """,
                new { Schema = schema }
            );
            objects
                .Should()
                .BeEquivalentTo(
                    "messaging_published.correlation_id",
                    "messaging_published.causation_id",
                    "messaging_received.correlation_id",
                    "messaging_received.causation_id",
                    "idx_messaging_published_correlation",
                    "idx_messaging_received_correlation"
                );
        }
        finally
        {
            await using var cleanup = new NpgsqlConnection(fixture.ConnectionString);
            await cleanup.OpenAsync(AbortToken);
            await cleanup.ExecuteAsync($"""DROP SCHEMA IF EXISTS "{schema}" CASCADE;""");
        }
    }

    private async Task<IReadOnlyList<string>> _ReadHistoryAsync(string schema)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        return
        [
            .. await connection.QueryAsync<string>(
                $"""
                SELECT step_version || ':' || checksum || ':' || applied_at::text
                FROM "{schema}".headless_schema_history WHERE feature='Messaging' ORDER BY step_version;
                """
            ),
        ];
    }
}
