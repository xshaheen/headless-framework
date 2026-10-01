// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Abstractions;
using Headless.Coordination;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.Storage.PostgreSql;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Tests;

/// <summary>
/// A decision a statement makes on the database clock must read that clock after the statement has waited out the
/// row lock another transaction holds; a clock read before the wait would stamp a lease or due time in the past.
/// </summary>
[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlClockAfterLockTests(PostgreSqlTestFixture fixture) : TestBase
{
    private static readonly TimeSpan _LockHold = TimeSpan.FromSeconds(2);
    private readonly string _schema = $"clock_{Guid.NewGuid():N}";
    private IDataStorage _storage = null!;
    private IStorageTableNames _tableNames = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        var messagingOptions = Options.Create(new MessagingOptions { Version = "v1" });
        var options = new PostgreSqlOptions { ConnectionString = fixture.ConnectionString };
        _tableNames = TestStorageOptions.TableNames(_schema);
        await TestMessagingSchema.ApplyAsync(options, _schema, AbortToken);
        _storage = new RelationalDataStorage(
            Options.Create(options).Value.ToStorage(),
            messagingOptions,
            TestStorageOptions.For(_schema),
            _tableNames,
            new JsonUtf8Serializer(messagingOptions),
            new SequentialGuidGenerator(SequentialGuidType.Version7),
            TimeProvider.System,
            new NullNodeMembership(),
            NullLogger<RelationalDataStorage>.Instance
        );
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.ExecuteAsync($"""DROP SCHEMA IF EXISTS "{_schema}" CASCADE;""");
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
        var lockedUntil = await _ReadInstantAsync("locked_until", stored.StorageId);
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
        var nextRetryAt = await _ReadInstantAsync("next_retry_at", stored.StorageId);
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
        await using var holder = new NpgsqlConnection(fixture.ConnectionString);
        await holder.OpenAsync(AbortToken);
        await using var transaction = await holder.BeginTransactionAsync(AbortToken);
        await holder.ExecuteAsync(
            new CommandDefinition(
                $"""SELECT 1 FROM {_tableNames.GetPublishedTableName()} WHERE "id"=@Id FOR UPDATE;""",
                new { Id = id },
                transaction,
                cancellationToken: AbortToken
            )
        );

        var pending = action();
        await Task.Delay(_LockHold, AbortToken);
        pending.IsCompleted.Should().BeFalse("the statement must still be waiting for the row lock");

        var released = _Utc(
            await holder.QuerySingleAsync<DateTime>(
                new CommandDefinition(
                    "SELECT clock_timestamp();",
                    transaction: transaction,
                    cancellationToken: AbortToken
                )
            )
        );
        await transaction.CommitAsync(AbortToken);

        return (await pending, released);
    }

    private async Task<DateTimeOffset> _ReadInstantAsync(string column, Guid id)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);

        return _Utc(
            await connection.QuerySingleAsync<DateTime>(
                new CommandDefinition(
                    $"""SELECT "{column}" FROM {_tableNames.GetPublishedTableName()} WHERE "id"=@Id;""",
                    new { Id = id },
                    cancellationToken: AbortToken
                )
            )
        );
    }

    private static DateTimeOffset _Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
