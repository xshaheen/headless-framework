// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless;
using Headless.Coordination;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.SqlServer;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// SqlClient reports a cancel that interrupts a running statement as a <see cref="SqlException" />, where Npgsql and
/// Microsoft.Data.Sqlite throw <see cref="OperationCanceledException" />. Each test holds the message's row lock from
/// another transaction, so the cancel always lands while the storage's command waits on the server.
/// </summary>
[Collection<SqlServerTestFixture>]
public sealed class SqlServerCancellationTests(SqlServerTestFixture fixture) : TestBase
{
    private readonly string _schema = $"cancel_{Guid.NewGuid():N}";
    private IDataStorage _storage = null!;
    private IStorageTableNames _tableNames = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var messagingOptions = Options.Create(new MessagingOptions { Version = "v1" });
        var options = new SqlServerOptions { ConnectionString = fixture.ConnectionString };
        _tableNames = TestStorageOptions.TableNames(_schema);
        await TestMessagingSchema.ApplyAsync(options, _schema, AbortToken);
        _storage = new RelationalDataStorage(
            Options.Create(options).Value.ToStorage(),
            messagingOptions,
            TestStorageOptions.For(_schema),
            _tableNames,
            new JsonUtf8Serializer(messagingOptions),
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            new NullNodeMembership(),
            NullLogger<RelationalDataStorage>.Instance
        );
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.ExecuteAsync(TestMessagingSchema.DropSql(_schema));
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_throw_operation_canceled_when_a_revoke_is_cancelled_while_it_waits_on_a_held_row()
    {
        // given
        var message = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = Guid.NewGuid().ToString() },
            new { Data = "cancel" }
        );
        var stored = await _storage.StoreMessageAsync("cancel.topic", message, cancellationToken: AbortToken);

        await using var holder = new SqlConnection(fixture.ConnectionString);
        await holder.OpenAsync(AbortToken);
        await using var held = (SqlTransaction)await holder.BeginTransactionAsync(AbortToken);
        await holder.ExecuteAsync(
            new CommandDefinition(
                $"SELECT 1 FROM {_tableNames.GetPublishedTableName()} WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [Id]=@Id;",
                new { Id = stored.StorageId },
                held,
                cancellationToken: AbortToken
            )
        );
        var holderSession = await holder.QuerySingleAsync<short>(
            new CommandDefinition("SELECT CAST(@@SPID AS smallint);", transaction: held, cancellationToken: AbortToken)
        );

        // when: the revoke's DELETE queues on the held row, on a connection the storage opened for it
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var call = ((IMessageRevocationStorage)_storage).RevokeAsync(stored.StorageId, cts.Token).AsTask();

        await _WaitUntilBlockedAsync(holderSession);
        await cts.CancelAsync();

        // then
        var thrown = await FluentActions.Awaiting(() => call).Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.InnerException.Should().BeOfType<SqlException>();

        await held.RollbackAsync(AbortToken);
        (await ((IMessageRevocationStorage)_storage).RevokeAsync(stored.StorageId, AbortToken))
            .Should()
            .Be(MessageRevocationResult.AttemptReserved);
    }

    // The server waits until the storage's statement queues on the held row, so the cancel cannot reach the call before
    // its command does.
    private async Task _WaitUntilBlockedAsync(short holderSession)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                WHILE NOT EXISTS (SELECT 1 FROM sys.dm_exec_requests WHERE blocking_session_id = @Holder)
                    WAITFOR DELAY '00:00:00.010';
                """,
                new { Holder = holderSession },
                commandTimeout: 0,
                cancellationToken: AbortToken
            )
        );
    }
}
