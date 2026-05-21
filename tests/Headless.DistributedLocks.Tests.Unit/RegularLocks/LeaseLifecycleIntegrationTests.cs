// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Tests.Fakes;

namespace Tests.RegularLocks;

public sealed class LeaseLifecycleIntegrationTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly FakeDistributedLockStorage _storage = new();
    private readonly ILongIdGenerator _longIdGenerator = Substitute.For<ILongIdGenerator>();
    private long _lockIdCounter = 2000;

    [Fact]
    public async Task should_not_create_monitor_by_default()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        await using var handle = await provider.TryAcquireAsync(resource, cancellationToken: AbortToken);

        // then
        handle.Should().NotBeNull();
        handle!.HandleLostToken.Should().Be(CancellationToken.None);
        provider.GetActiveMonitorCount(resource).Should().Be(0);
    }

    [Fact]
    public async Task should_create_monitor_when_monitor_lease_is_enabled_and_deregister_on_dispose()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var handle = await provider.TryAcquireAsync(
            resource,
            timeUntilExpires: TimeSpan.FromSeconds(10),
            monitorLease: true,
            cancellationToken: AbortToken
        );

        // then
        handle.Should().NotBeNull();
        handle!.HandleLostToken.Should().NotBe(CancellationToken.None);
        provider.GetActiveMonitorCount(resource).Should().Be(1);

        await handle.DisposeAsync();
        provider.GetActiveMonitorCount(resource).Should().Be(0);
    }

    [Fact]
    public async Task should_cancel_handle_lost_token_when_monitored_lock_id_changes()
    {
        // given
        var options = new DistributedLockOptions();
        var provider = _CreateProvider(options);
        var resource = Faker.Random.AlphaNumeric(10);
        await using var handle = await provider.TryAcquireAsync(
            resource,
            timeUntilExpires: TimeSpan.FromSeconds(10),
            monitorLease: true,
            cancellationToken: AbortToken
        );
        handle.Should().NotBeNull();
        _storage.SetLock(options.KeyPrefix + resource, "foreign-lock", TimeSpan.FromSeconds(10));

        // when
        ((ICanReceiveLockReleased)provider).OnLockReleased(new DistributedLockReleased(resource, "foreign-lock"));
        await _DrainUntilAsync(() => handle!.HandleLostToken.IsCancellationRequested);

        // then
        handle!.HandleLostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_auto_promote_monitor_when_auto_extend_is_enabled()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        await using var handle = await provider.TryAcquireAsync(
            resource,
            timeUntilExpires: TimeSpan.FromSeconds(10),
            autoExtend: true,
            cancellationToken: AbortToken
        );

        // then
        handle.Should().NotBeNull();
        provider.GetActiveMonitorCount(resource).Should().Be(1);
        handle!.HandleLostToken.Should().NotBe(CancellationToken.None);
    }

    private DistributedLockProvider _CreateProvider(DistributedLockOptions? options = null)
    {
        _longIdGenerator.Create().Returns(_ => Interlocked.Increment(ref _lockIdCounter));

        return new DistributedLockProvider(
            _storage,
            Substitute.For<IOutboxPublisher>(),
            options ?? new DistributedLockOptions(),
            _longIdGenerator,
            _timeProvider,
            LoggerFactory.CreateLogger<DistributedLockProvider>()
        );
    }

    private static async Task _DrainUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 2000 && !condition(); i++)
        {
            if (i % 100 == 0)
            {
                await Task.Delay(1);
            }
            else
            {
                await Task.Yield();
            }
        }
    }
}
