// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Dapper;
using Headless.Abstractions;
using Headless.Coordination;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.Storage.SqlServer;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

[Collection<SqlServerTestFixture>]
public sealed class SqlServerPooledIsolationTests(SqlServerTestFixture fixture) : TestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_preserve_readpast_operations_after_serializable_inbox_admission(bool useSnapshot)
    {
        var databaseName = $"messaging_isolation_{Guid.NewGuid():N}";
        var masterOptions = new SqlConnectionStringBuilder(fixture.ConnectionString)
        {
            InitialCatalog = "master",
            Pooling = false,
        };
        var connectionOptions = new SqlConnectionStringBuilder(fixture.ConnectionString)
        {
            InitialCatalog = databaseName,
            ApplicationName = databaseName,
            Pooling = true,
            MaxPoolSize = 1,
        };
        await using var master = new SqlConnection(masterOptions.ConnectionString);
        await master.OpenAsync(AbortToken);
        await master.ExecuteAsync(
            new CommandDefinition($"CREATE DATABASE [{databaseName}];", cancellationToken: AbortToken)
        );

        try
        {
            if (useSnapshot)
            {
                await master.ExecuteAsync(
                    new CommandDefinition(
                        $"ALTER DATABASE [{databaseName}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;",
                        cancellationToken: AbortToken
                    )
                );
            }

            var messagingOptions = Options.Create(new MessagingOptions { Version = "v1", SchedulerBatchSize = 10 });
            var sqlOptions = Options.Create(
                new SqlServerOptions { ConnectionString = connectionOptions.ConnectionString, Schema = "messaging" }
            );
            var initializer = new SqlServerStorageInitializer(
                NullLogger<SqlServerStorageInitializer>.Instance,
                sqlOptions,
                messagingOptions
            );
            await initializer.InitializeAsync(AbortToken);
            var serializer = new JsonUtf8Serializer(messagingOptions);
            var storage = new SqlServerDataStorage(
                messagingOptions,
                sqlOptions,
                initializer,
                serializer,
                new SequentialGuidGenerator(SequentialGuidType.SqlServer),
                TimeProvider.System,
                new NullNodeMembership(),
                NullLogger<SqlServerDataStorage>.Instance
            );
            var monitoring = storage.GetMonitoringApi();
            var expiredId = Guid.NewGuid();
            var lockedId = Guid.NewGuid();
            var delayedId = Guid.NewGuid();
            var queuedId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            int sessionId;
            await using (var connection = new SqlConnection(connectionOptions.ConnectionString))
            {
                await connection.OpenAsync(AbortToken);
                sessionId = connection.ServerProcessId;
                (
                    await connection.ExecuteScalarAsync<bool>(
                        new CommandDefinition(
                            "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name=DB_NAME();",
                            cancellationToken: AbortToken
                        )
                    )
                )
                    .Should()
                    .Be(useSnapshot);
                await _InsertPublishedAsync(
                    connection,
                    serializer,
                    expiredId,
                    StatusName.Succeeded,
                    now.AddMinutes(-5)
                );
                await _InsertPublishedAsync(connection, serializer, lockedId, StatusName.Succeeded, now.AddDays(1));
                await _InsertPublishedAsync(connection, serializer, delayedId, StatusName.Delayed, now.AddMinutes(-5));
                await _InsertPublishedAsync(connection, serializer, queuedId, StatusName.Queued, now.AddMinutes(-5));
            }

            var received = await _AdmitOnPooledSessionAsync(storage, connectionOptions.ConnectionString, sessionId);
            // A separate, unpooled locker leaves the single pooled connection available to the APIs under test.
            var lockOptions = new SqlConnectionStringBuilder(connectionOptions.ConnectionString) { Pooling = false };
            await using (var locker = new SqlConnection(lockOptions.ConnectionString))
            {
                await locker.OpenAsync(AbortToken);
                await using var lockedTransaction = await locker.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    AbortToken
                );
                await locker.ExecuteAsync(
                    new CommandDefinition(
                        "UPDATE messaging.Published WITH (ROWLOCK) SET Content=Content WHERE Id=@Id;",
                        new { Id = lockedId },
                        transaction: lockedTransaction,
                        cancellationToken: AbortToken
                    )
                );
                (await monitoring.GetPublishedMessageAsync(expiredId, AbortToken))!.StorageId.Should().Be(expiredId);
                (await monitoring.GetPublishedMessageAsync(lockedId, AbortToken)).Should().BeNull();
                (await monitoring.GetPublishedMessagesAsync([expiredId, lockedId], AbortToken))
                    .Should()
                    .ContainSingle(message => message.StorageId == expiredId);
                (await monitoring.GetReceivedMessageAsync(received.Message.StorageId, AbortToken))!
                    .StorageId.Should()
                    .Be(received.Message.StorageId);
                (await monitoring.GetReceivedMessagesAsync([received.Message.StorageId], AbortToken))
                    .Should()
                    .ContainSingle(message => message.StorageId == received.Message.StorageId);
                await lockedTransaction.RollbackAsync(AbortToken);
            }

            (await storage.DeleteExpiresAsync(initializer.GetPublishedTableName(), now, 10, AbortToken)).Should().Be(1);
            (await monitoring.GetPublishedMessageAsync(expiredId, AbortToken)).Should().BeNull();
            (await monitoring.GetPublishedMessageAsync(lockedId, AbortToken)).Should().NotBeNull();

            await _AdmitOnPooledSessionAsync(storage, connectionOptions.ConnectionString, sessionId);
            var scheduled = new List<MediumMessage>();
            await storage.ScheduleMessagesOfDelayedAsync(
                (_, messages) =>
                {
                    scheduled.AddRange(messages);
                    return ValueTask.CompletedTask;
                },
                AbortToken
            );
            scheduled.Select(message => message.StorageId).Should().BeEquivalentTo([delayedId, queuedId]);

            await _AdmitOnPooledSessionAsync(storage, connectionOptions.ConnectionString, sessionId);
            var claimed = await storage.ClaimDelayedMessagesAsync(AbortToken);
            claimed.Select(message => message.StorageId).Should().BeEquivalentTo([delayedId, queuedId]);
            (await storage.ClaimDelayedMessagesAsync(AbortToken)).Should().BeEmpty();
        }
        finally
        {
            using var pool = new SqlConnection(connectionOptions.ConnectionString);
            SqlConnection.ClearPool(pool);
            await master.ExecuteAsync(
                new CommandDefinition(
                    $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];",
                    cancellationToken: AbortToken
                )
            );
        }
    }

    private static async Task<InboxAdmissionResult> _AdmitOnPooledSessionAsync(
        SqlServerDataStorage storage,
        string connectionString,
        int sessionId
    )
    {
        var admitted = await storage.AdmitReceivedMessageAsync(
            "isolation.message",
            "isolation-group",
            "isolation.consumer",
            "1",
            new MediumMessage
            {
                StorageId = Guid.Empty,
                Origin = _Message(Guid.NewGuid()),
                Lane = MessageLane.Bus,
                Content = string.Empty,
            },
            cancellationToken: AbortToken
        );
        admitted.Disposition.Should().Be(InboxAdmissionDisposition.Winner);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(AbortToken);
        connection.ServerProcessId.Should().Be(sessionId, "the pool has exactly one physical connection");
        (
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    "SELECT transaction_isolation_level FROM sys.dm_exec_sessions WHERE session_id=@@SPID;",
                    cancellationToken: AbortToken
                )
            )
        )
            .Should()
            .Be(4, "inbox admission leaves Serializable isolation on the reused session");
        return admitted;
    }

    private static Task _InsertPublishedAsync(
        SqlConnection connection,
        ISerializer serializer,
        Guid id,
        StatusName status,
        DateTimeOffset expiresAt
    ) =>
        connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO messaging.Published
                    (Id,Version,Name,Content,IntentType,Retries,Added,ExpiresAt,NextRetryAt,LockedUntil,Owner,StatusName,MessageId)
                VALUES (@Id,'v1','isolation.message',@Content,0,0,SYSUTCDATETIME(),@ExpiresAt,NULL,NULL,NULL,@StatusName,@MessageId);
                """,
                new
                {
                    Id = id,
                    Content = serializer.Serialize(_Message(id)),
                    ExpiresAt = expiresAt,
                    StatusName = status.ToString(),
                    MessageId = id.ToString(),
                },
                cancellationToken: AbortToken
            )
        );

    private static Message _Message(Guid id) =>
        new(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = id.ToString(),
                [Headers.MessageName] = "isolation.message",
            },
            new { Data = "test" }
        );
}
