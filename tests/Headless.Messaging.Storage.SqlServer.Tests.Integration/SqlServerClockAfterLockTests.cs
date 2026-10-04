// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Abstractions;
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
/// A decision a statement makes on the database clock must read that clock after the statement has waited out the
/// row lock another transaction holds; a clock read before the wait would stamp a lease or due time in the past.
/// </summary>
[Collection<SqlServerTestFixture>]
public sealed class SqlServerClockAfterLockTests(SqlServerTestFixture fixture) : TestBase
{
    private static readonly TimeSpan _LockHold = TimeSpan.FromSeconds(2);
    private readonly string _schema = $"clock_{Guid.NewGuid():N}";
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
    public async Task should_stamp_lease_from_clock_read_after_row_lock_wait()
    {
        // given
        var stored = await _StorePublishedAsync();
        var lease = TimeSpan.FromMinutes(1);

        // when
        var (acquired, released) = await _RunWhileRowLockedAsync(
            stored.StorageId,
            () => _storage.LeasePublishAsync(stored, lease, AbortToken).AsTask()
        );

        // then
        acquired.Should().BeTrue();
        var lockedUntil = await _ReadInstantAsync("LockedUntil", stored.StorageId);
        (lockedUntil - lease).Should().BeOnOrAfter(released);
    }

    [Fact]
    public async Task should_decide_retry_due_time_from_clock_read_after_row_lock_wait()
    {
        // given
        var stored = await _StorePublishedAsync();
        var delay = TimeSpan.FromMinutes(5);

        // when
        var (changed, released) = await _RunWhileRowLockedAsync(
            stored.StorageId,
            () =>
                _storage
                    .ChangePublishStateAsync(
                        stored,
                        StatusName.Failed,
                        retryDelay: RetryDelay.Exactly(delay),
                        cancellationToken: AbortToken
                    )
                    .AsTask()
        );

        // then
        changed.Should().BeTrue();
        var nextRetryAt = await _ReadInstantAsync("NextRetryAt", stored.StorageId);
        (nextRetryAt - delay).Should().BeOnOrAfter(released);
    }

    private async Task<MediumMessage> _StorePublishedAsync()
    {
        var message = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal) { [Headers.MessageId] = Guid.NewGuid().ToString() },
            new { Data = "clock" }
        );

        return await _storage.StoreMessageAsync("clock.topic", message, cancellationToken: AbortToken);
    }

    /// <summary>
    /// Holds the row's lock from another transaction while <paramref name="action"/> runs, so the action's statement
    /// waits for it, then releases it and returns the database clock read just before the release.
    /// </summary>
    private async Task<(T Result, DateTimeOffset Released)> _RunWhileRowLockedAsync<T>(Guid id, Func<Task<T>> action)
    {
        await using var holder = new SqlConnection(fixture.ConnectionString);
        await holder.OpenAsync(AbortToken);
        await using var transaction = (SqlTransaction)await holder.BeginTransactionAsync(AbortToken);
        await holder.ExecuteAsync(
            new CommandDefinition(
                $"SELECT 1 FROM {_tableNames.GetPublishedTableName()} WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [Id]=@Id;",
                new { Id = id },
                transaction,
                cancellationToken: AbortToken
            )
        );

        var pending = action();
        await Task.Delay(_LockHold, AbortToken);
        pending.IsCompleted.Should().BeFalse("the statement must still be waiting for the row lock");

        var released = await holder.QuerySingleAsync<DateTimeOffset>(
            new CommandDefinition(
                "SELECT TODATETIMEOFFSET(SYSUTCDATETIME(), 0);",
                transaction: transaction,
                cancellationToken: AbortToken
            )
        );
        await transaction.CommitAsync(AbortToken);

        return (await pending, released);
    }

    private async Task<DateTimeOffset> _ReadInstantAsync(string column, Guid id)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);

        return await connection.QuerySingleAsync<DateTimeOffset>(
            new CommandDefinition(
                $"SELECT [{column}] FROM {_tableNames.GetPublishedTableName()} WHERE [Id]=@Id;",
                new { Id = id },
                cancellationToken: AbortToken
            )
        );
    }
}
