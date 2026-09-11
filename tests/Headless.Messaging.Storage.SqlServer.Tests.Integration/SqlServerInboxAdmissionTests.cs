// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Xml.Linq;
using Dapper;
using Headless.Abstractions;
using Headless.Coordination;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.Storage.SqlServer;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerInboxAdmissionTests(SqlServerTestFixture fixture, ITestOutputHelper output) : TestBase
{
    [Fact]
    public async Task should_seek_root_hash_index_for_concurrent_admissions_into_populated_inbox()
    {
        var schema = $"admission_{Guid.NewGuid():N}";
        var messagingOptions = Options.Create(new MessagingOptions { Version = "v1" });
        var sqlOptions = Options.Create(
            new SqlServerOptions { ConnectionString = fixture.ConnectionString, Schema = schema }
        );
        var initializer = new SqlServerStorageInitializer(
            NullLogger<SqlServerStorageInitializer>.Instance,
            sqlOptions,
            messagingOptions
        );
        await initializer.InitializeAsync(AbortToken);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);

        try
        {
            var storage = new SqlServerDataStorage(
                messagingOptions,
                sqlOptions,
                initializer,
                new JsonUtf8Serializer(messagingOptions),
                new SequentialGuidGenerator(SequentialGuidType.SqlServer),
                TimeProvider.System,
                new NullNodeMembership(),
                NullLogger<SqlServerDataStorage>.Instance
            );
            const int retainedCount = 512;
            for (var index = 0; index < retainedCount; index++)
            {
                (await _AdmitAsync(storage, $"retained-{index}"))
                    .Disposition.Should()
                    .Be(InboxAdmissionDisposition.Winner);
            }

            await connection.ExecuteAsync(
                new CommandDefinition(
                    $"UPDATE STATISTICS [{schema}].[Received] WITH FULLSCAN;",
                    cancellationToken: AbortToken
                )
            );

            const int concurrentCount = 16;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = Enumerable
                .Range(0, concurrentCount)
                .Select(async index =>
                {
                    await start.Task.WaitAsync(AbortToken);
                    return await _AdmitAsync(storage, $"concurrent-{index}");
                })
                .ToArray();
            start.SetResult();
            var admissions = await Task.WhenAll(pending);

            admissions.Should().OnlyContain(result => result.Disposition == InboxAdmissionDisposition.Winner);
            admissions.Select(result => result.Message.StorageId).Should().OnlyHaveUniqueItems();
            for (var index = 0; index < concurrentCount; index++)
            {
                var duplicate = await _AdmitAsync(storage, $"concurrent-{index}");
                duplicate.Disposition.Should().Be(InboxAdmissionDisposition.InFlightDuplicate);
                duplicate.Message.StorageId.Should().Be(admissions[index].Message.StorageId);
                duplicate.Message.Origin.Id.Should().Be($"concurrent-{index}");
            }

            (
                await connection.ExecuteScalarAsync<int>(
                    new CommandDefinition($"SELECT COUNT(*) FROM [{schema}].[Received];", cancellationToken: AbortToken)
                )
            )
                .Should()
                .Be(retainedCount + concurrentCount);

            // Inspect the plan compiled for the provider's actual read, so a copied test query cannot hide a regression.
            var plans = (
                await connection.QueryAsync<(string Sql, string Plan)>(
                    new CommandDefinition(
                        """
                        SELECT sqlText.text AS [Sql], CONVERT(nvarchar(max), queryPlan.query_plan) AS [Plan]
                        FROM sys.dm_exec_cached_plans AS cached
                        CROSS APPLY sys.dm_exec_sql_text(cached.plan_handle) AS sqlText
                        CROSS APPLY sys.dm_exec_query_plan(cached.plan_handle) AS queryPlan
                        WHERE CHARINDEX(@ReadPrefix, sqlText.text) > 0
                          AND CHARINDEX(@TableName, sqlText.text) > 0;
                        """,
                        new
                        {
                            ReadPrefix = "SELECT [Id],[Content],[IntentType],[Retries],[InlineAttempts]",
                            TableName = initializer.GetReceivedTableName(),
                        },
                        cancellationToken: AbortToken
                    )
                )
            ).ToList();
            plans.Should().NotBeEmpty();
            XNamespace showplan = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
            foreach (var (sql, plan) in plans)
            {
                sql.Should().Contain("@InboxKeyHash binary(32)");
                var hashSeeks = XDocument
                    .Parse(plan)
                    .Descendants(showplan + "RelOp")
                    .Where(operation => (string?)operation.Attribute("PhysicalOp") == "Index Seek")
                    .SelectMany(operation => operation.Elements(showplan + "IndexScan"))
                    .Where(scan =>
                        (string?)scan.Element(showplan + "Object")?.Attribute("Index")
                        == $"[UX_{schema}_Received_InboxRootKey]"
                    )
                    .ToList();
                hashSeeks.Should().ContainSingle();
                hashSeeks[0]
                    .Descendants(showplan + "SeekPredicates")
                    .Descendants(showplan + "ColumnReference")
                    .Should()
                    .Contain(column => (string?)column.Attribute("Column") == "InboxKeyHash");
                output.WriteLine(plan);
            }

            output.WriteLine(
                $"Retained: {retainedCount}; concurrent winners: {admissions.Length}; duplicates: {concurrentCount}."
            );
        }
        finally
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    $"""
                    DROP TABLE IF EXISTS [{schema}].InboxAudit;
                    DROP TABLE IF EXISTS [{schema}].InboxOperationReceipts;
                    DROP TABLE IF EXISTS [{schema}].SchemaState;
                    DROP TABLE IF EXISTS [{schema}].Published;
                    DROP TABLE IF EXISTS [{schema}].Received;
                    DROP TABLE IF EXISTS [{schema}].Lock;
                    DROP TYPE [{schema}].[HeadlessMessagingIdList];
                    DROP TYPE [{schema}].[HeadlessMessagingOwnerList];
                    DROP TYPE [{schema}].[HeadlessMessagingPoisonMessageList];
                    DROP SCHEMA [{schema}];
                    """,
                    cancellationToken: AbortToken
                )
            );
        }
    }

    private static ValueTask<InboxAdmissionResult> _AdmitAsync(IDataStorage storage, string messageId) =>
        storage.AdmitReceivedMessageAsync(
            "orders.created",
            "orders-group",
            "orders.consumer",
            "v1",
            new MediumMessage
            {
                StorageId = Guid.NewGuid(),
                Origin = new Message(
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        [Headers.MessageId] = messageId,
                        [Headers.MessageName] = "orders.created",
                    },
                    new { Data = "admission-test" }
                ),
                Content = string.Empty,
                Lane = MessageLane.Bus,
            },
            cancellationToken: AbortToken
        );
}
