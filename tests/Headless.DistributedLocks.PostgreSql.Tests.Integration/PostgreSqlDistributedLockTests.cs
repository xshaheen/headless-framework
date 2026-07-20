// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.PostgreSql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlDistributedLockFixture>]
public sealed class PostgreSqlDistributedLockTests(PostgreSqlDistributedLockFixture fixture) : TestBase
{
    [Fact]
    public async Task should_acquire_release_and_issue_monotonic_fencing_tokens()
    {
        await using var provider = _CreateProvider();
        var locks = provider.GetRequiredService<IDistributedLock>();
        var resource = Faker.Random.AlphaNumeric(12);

        await using var first = await locks.AcquireAsync(resource, cancellationToken: AbortToken);
        var firstToken = first.FencingToken;
        await first.ReleaseAsync();

        await using var second = await locks.AcquireAsync(resource, cancellationToken: AbortToken);

        firstToken.Should().NotBeNull();
        second.FencingToken.Should().BeGreaterThan(firstToken!.Value);
    }

    [Fact]
    public async Task should_return_null_expiration_for_session_scoped_lock()
    {
        await using var provider = _CreateProvider();
        var locks = provider.GetRequiredService<IDistributedLock>();
        var resource = Faker.Random.AlphaNumeric(12);

        await using var handle = await locks.AcquireAsync(resource, cancellationToken: AbortToken);

        (await locks.GetExpirationAsync(resource, AbortToken)).Should().BeNull();
        handle.CanObserveLoss.Should().BeFalse();
        handle.LostToken.Should().Be(CancellationToken.None);
    }

    [Fact]
    public async Task should_report_remote_holder_without_remote_lease_id_on_resource_targeted_inspection()
    {
        var keyPrefix = $"test:inspect:{Faker.Random.AlphaNumeric(6)}:";
        var resource = Faker.Random.AlphaNumeric(12);

        await using var ownerProvider = _CreateProvider(options => options.KeyPrefix = keyPrefix);
        await using var observerProvider = _CreateProvider(options => options.KeyPrefix = keyPrefix);

        var owner = ownerProvider.GetRequiredService<IDistributedLock>();
        var observer = observerProvider.GetRequiredService<IDistributedLock>();

        await using var handle = await owner.AcquireAsync(resource, cancellationToken: AbortToken);

        (await observer.GetLeaseIdAsync(resource, AbortToken)).Should().BeNull();
        (await observer.IsLockedAsync(resource, AbortToken)).Should().BeTrue();

        var info = await observer.GetLockInfoAsync(resource, AbortToken);

        info.Should().NotBeNull();
        info!.Resource.Should().Be(resource);
        info.LeaseId.Should().BeNull();
        info.TimeToLive.Should().BeNull();
    }

    [Fact]
    public async Task should_report_reader_and_writer_state_by_lock_mode()
    {
        await using var provider = _CreateProvider();
        var locks = provider.GetRequiredService<IDistributedReadWriteLock>();
        var resource = Faker.Random.AlphaNumeric(12);

        await using (var firstReader = await locks.AcquireReadLockAsync(resource, cancellationToken: AbortToken))
        await using (var secondReader = await locks.AcquireReadLockAsync(resource, cancellationToken: AbortToken))
        {
            (await locks.IsReadLockedAsync(resource, AbortToken)).Should().BeTrue();
            (await locks.IsWriteLockedAsync(resource, AbortToken)).Should().BeFalse();
            (await locks.GetReaderCountAsync(resource, AbortToken)).Should().Be(2);
        }

        await using var writer = await locks.AcquireWriteLockAsync(resource, cancellationToken: AbortToken);

        (await locks.IsReadLockedAsync(resource, AbortToken)).Should().BeFalse();
        (await locks.IsWriteLockedAsync(resource, AbortToken)).Should().BeTrue();
        (await locks.GetReaderCountAsync(resource, AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task should_not_publish_postgres_notification_when_push_wakeup_is_disabled()
    {
        const string channel = "headless_distributed_locks_release";
        const string sentinelPayload = "wakeup-disabled-fence";

        await using var listener = new NpgsqlConnection(fixture.ConnectionString);
        await listener.OpenAsync(AbortToken);

        var releaseNotifications = 0;
        var sentinelSeen = false;
        listener.Notification += (_, args) =>
        {
            if (!string.Equals(args.Channel, channel, StringComparison.Ordinal))
            {
                return;
            }

            // The sentinel is the synchronization fence: NOTIFY delivery on a single connection is
            // ordered, so once we observe our own sentinel every notification the release could have
            // emitted (it runs strictly before the sentinel publish) has already been delivered. No
            // wall-clock wait is needed.
            if (string.Equals(args.Payload, sentinelPayload, StringComparison.Ordinal))
            {
                sentinelSeen = true;
            }
            else
            {
                releaseNotifications++;
            }
        };

        await using (var command = listener.CreateCommand())
        {
            command.CommandText = $"LISTEN {channel}";
            await command.ExecuteNonQueryAsync(AbortToken);
        }

        await using var provider = _CreateProvider(options => options.EnablePushWakeup = false);
        var locks = provider.GetRequiredService<IDistributedLock>();
        var resource = Faker.Random.AlphaNumeric(12);
        await using var handle = await locks.AcquireAsync(resource, cancellationToken: AbortToken);

        await handle.ReleaseAsync();

        // Publish our own sentinel on the same channel from a separate connection, then drain the
        // listener until it arrives. Any framework-emitted release notification would be ordered before
        // the sentinel and counted by then.
        await using (var sentinelConnection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await sentinelConnection.OpenAsync(AbortToken);
            await using var notify = sentinelConnection.CreateCommand();
            notify.CommandText = $"SELECT pg_notify('{channel}', @payload)";
            notify.Parameters.AddWithValue("payload", sentinelPayload);
            await notify.ExecuteNonQueryAsync(AbortToken);
        }

        while (!sentinelSeen)
        {
            await listener.WaitAsync(AbortToken);
        }

        releaseNotifications.Should().Be(0);
    }

    private ServiceProvider _CreateProvider(Action<PostgreSqlDistributedLockOptions>? configure = null)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup =>
            setup.UsePostgreSql(options =>
            {
                options.ConnectionString = fixture.ConnectionString;
                options.KeyPrefix = "test:";
                configure?.Invoke(options);
            })
        );

        return services.BuildServiceProvider();
    }
}
