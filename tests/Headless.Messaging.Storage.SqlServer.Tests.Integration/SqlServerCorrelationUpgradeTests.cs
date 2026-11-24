// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Hosting;
using Headless.Hosting.Schema;
using Headless.Messaging;
using Headless.Messaging.Storage.SqlServer;
using Headless.Sql.SqlServer;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// A database that already ran the steps of the build before the correlation columns: its host refuses to start until
/// the new step is provisioned, and provisioning applies that step alone.
/// </summary>
[Collection<SqlServerTestFixture>]
public sealed class SqlServerCorrelationUpgradeTests(SqlServerTestFixture fixture) : TestBase
{
    [Fact]
    public async Task should_fail_a_start_before_provisioning_and_apply_only_the_correlation_step_after()
    {
        // given: a database at the steps the previous build shipped
        var schema = $"msg_upgrade_{Guid.NewGuid():N}"[..24];
        var options = new SqlServerOptions { ConnectionString = fixture.ConnectionString };
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
                        SqlServerMessagingSchemaContribution.CorrelationStepVersion,
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
            setup.UseSqlServer(fixture.ConnectionString);
        });
        await using var provider = services.BuildServiceProvider();

        try
        {
            var before = await new SchemaRunner([shipped]).ApplyAsync(AbortToken);
            before.AppliedSteps.Select(static step => step.Version).Should().Equal("1", "2");
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
                .Contain($"Steps {SqlServerMessagingSchemaContribution.CorrelationStepVersion} of Messaging");

            // when: provisioning runs
            var provisioned = await new SchemaRunner([current]).ApplyAsync(AbortToken);

            // then: only the new step ran, the recorded ones are untouched, and the columns and indexes exist
            provisioned
                .AppliedSteps.Should()
                .Equal(
                    new SchemaAppliedStep(
                        schema,
                        "Messaging",
                        SqlServerMessagingSchemaContribution.CorrelationStepVersion
                    )
                );
            (await _ReadHistoryAsync(schema)).Take(historyBefore.Count).Should().Equal(historyBefore);
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync(AbortToken);
            var objects = await connection.QueryAsync<string>(
                """
                SELECT OBJECT_NAME(object_id) + N'.' + name FROM sys.columns
                WHERE object_id IN (OBJECT_ID(QUOTENAME(@Schema) + N'.MessagingPublished'), OBJECT_ID(QUOTENAME(@Schema) + N'.MessagingReceived'))
                  AND name IN (N'CorrelationId', N'CausationId')
                UNION ALL
                SELECT name FROM sys.indexes
                WHERE object_id IN (OBJECT_ID(QUOTENAME(@Schema) + N'.MessagingPublished'), OBJECT_ID(QUOTENAME(@Schema) + N'.MessagingReceived'))
                  AND name LIKE N'IX_Messaging%_Correlation';
                """,
                new { Schema = schema }
            );
            objects
                .Should()
                .BeEquivalentTo(
                    "MessagingPublished.CorrelationId",
                    "MessagingPublished.CausationId",
                    "MessagingReceived.CorrelationId",
                    "MessagingReceived.CausationId",
                    "IX_MessagingPublished_Correlation",
                    "IX_MessagingReceived_Correlation"
                );
        }
        finally
        {
            await using var cleanup = new SqlConnection(fixture.ConnectionString);
            await cleanup.OpenAsync(AbortToken);
            await cleanup.ExecuteAsync(TestMessagingSchema.DropSql(schema));
        }
    }

    private async Task<IReadOnlyList<string>> _ReadHistoryAsync(string schema)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        return
        [
            .. await connection.QueryAsync<string>(
                $"""
                SELECT CAST(StepVersion AS nvarchar(50)) COLLATE DATABASE_DEFAULT + N':' + Checksum + N':' + CONVERT(nvarchar(40), AppliedAt, 127)
                FROM [{schema}].headless_schema_history WHERE Feature=N'Messaging' ORDER BY StepVersion;
                """
            ),
        ];
    }
}
