// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Headless.Abstractions;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.PostgreSql;
using Headless.Messaging.Serialization;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlTestFixture>]
public sealed class PostgreSqlLockTest(PostgreSqlTestFixture fixture) : TestBase
{
    private PostgreSqlDataStorage _storage = null!;
    private TimeProvider _timeProvider = null!;

    public override async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.Configure<PostgreSqlOptions>(x => x.ConnectionString = fixture.ConnectionString);
        services.Configure<MessagingOptions>(x =>
        {
            x.Version = "v1";
            x.UseStorageLock = true;
        });
        services.AddSingleton<IStorageInitializer, PostgreSqlStorageInitializer>();
        services.AddSingleton<ISerializer, JsonUtf8Serializer>();
        services.AddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator());
        services.AddSingleton(TimeProvider.System);

        var provider = services.BuildServiceProvider();
        var initializer = provider.GetRequiredService<IStorageInitializer>();
        await initializer.InitializeAsync();

        _timeProvider = provider.GetRequiredService<TimeProvider>();
        _storage = new PostgreSqlDataStorage(
            provider.GetRequiredService<IOptions<PostgreSqlOptions>>(),
            provider.GetRequiredService<IOptions<MessagingOptions>>(),
            initializer,
            provider.GetRequiredService<ISerializer>(),
            provider.GetRequiredService<ILongIdGenerator>(),
            _timeProvider
        );

        await base.InitializeAsync();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        // Reset lock table
        await connection.ExecuteAsync(
            "UPDATE messaging.lock SET \"Instance\"='', \"LastLockTime\"='0001-01-01 00:00:00'"
        );
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_acquire_lock_when_not_held()
    {
        // given
        const string key = "publish_retry_v1";
        var instance = Guid.NewGuid().ToString();
        var ttl = TimeSpan.FromMinutes(5);

        // when
        var acquired = await _storage.AcquireLockAsync(key, ttl, instance, AbortToken);

        // then
        acquired.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_acquire_lock_when_held_by_another_instance()
    {
        // given
        const string key = "publish_retry_v1";
        var instance1 = Guid.NewGuid().ToString();
        var instance2 = Guid.NewGuid().ToString();
        var ttl = TimeSpan.FromMinutes(5);

        // Acquire lock with first instance
        await _storage.AcquireLockAsync(key, ttl, instance1, AbortToken);

        // when - try to acquire with second instance
        var acquired = await _storage.AcquireLockAsync(key, ttl, instance2, AbortToken);

        // then
        acquired.Should().BeFalse();
    }

    [Fact]
    public async Task should_acquire_lock_after_ttl_expires()
    {
        // given
        const string key = "publish_retry_v1";
        var instance1 = Guid.NewGuid().ToString();
        var instance2 = Guid.NewGuid().ToString();

        // Acquire lock with short TTL
        var shortTtl = TimeSpan.FromMilliseconds(10);
        await _storage.AcquireLockAsync(key, shortTtl, instance1, AbortToken);

        // Wait for TTL to expire
        await Task.Delay(TimeSpan.FromMilliseconds(50), AbortToken);

        // when - try to acquire with second instance (TTL determines how old lock must be to be considered stale)
        var acquired = await _storage.AcquireLockAsync(key, TimeSpan.FromMilliseconds(30), instance2, AbortToken);

        // then
        acquired.Should().BeTrue();
    }

    [Fact]
    public async Task should_release_lock()
    {
        // given
        const string key = "publish_retry_v1";
        var instance = Guid.NewGuid().ToString();
        var ttl = TimeSpan.FromMinutes(5);

        await _storage.AcquireLockAsync(key, ttl, instance, AbortToken);

        // when
        await _storage.ReleaseLockAsync(key, instance, AbortToken);

        // then - another instance should be able to acquire
        var newInstance = Guid.NewGuid().ToString();
        var acquired = await _storage.AcquireLockAsync(key, ttl, newInstance, AbortToken);
        acquired.Should().BeTrue();
    }

    [Fact]
    public async Task should_not_release_lock_held_by_different_instance()
    {
        // given
        const string key = "publish_retry_v1";
        var instance1 = Guid.NewGuid().ToString();
        var instance2 = Guid.NewGuid().ToString();
        var ttl = TimeSpan.FromMinutes(5);

        await _storage.AcquireLockAsync(key, ttl, instance1, AbortToken);

        // when - try to release with different instance
        await _storage.ReleaseLockAsync(key, instance2, AbortToken);

        // then - third instance should not be able to acquire (lock still held by instance1)
        var instance3 = Guid.NewGuid().ToString();
        var acquired = await _storage.AcquireLockAsync(key, ttl, instance3, AbortToken);
        acquired.Should().BeFalse();
    }

    [Fact]
    public async Task should_renew_lock()
    {
        // given
        const string key = "publish_retry_v1";
        var instance = Guid.NewGuid().ToString();
        var ttl = TimeSpan.FromMinutes(5);

        await _storage.AcquireLockAsync(key, ttl, instance, AbortToken);

        // when
        await _storage.RenewLockAsync(key, ttl, instance, AbortToken);

        // then - lock should still be held
        var otherInstance = Guid.NewGuid().ToString();
        var acquired = await _storage.AcquireLockAsync(key, ttl, otherInstance, AbortToken);
        acquired.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_renew_lock_held_by_different_instance()
    {
        // given
        const string key = "publish_retry_v1";
        var instance1 = Guid.NewGuid().ToString();
        var instance2 = Guid.NewGuid().ToString();

        // Acquire with short TTL
        var shortTtl = TimeSpan.FromMilliseconds(10);
        await _storage.AcquireLockAsync(key, shortTtl, instance1, AbortToken);

        // when - try to renew with different instance (should not extend TTL)
        await _storage.RenewLockAsync(key, TimeSpan.FromMinutes(5), instance2, AbortToken);

        // Wait for original TTL to expire
        await Task.Delay(TimeSpan.FromMilliseconds(50), AbortToken);

        // then - instance3 should be able to acquire because renewal failed (TTL determines staleness threshold)
        var instance3 = Guid.NewGuid().ToString();
        var acquired = await _storage.AcquireLockAsync(key, TimeSpan.FromMilliseconds(30), instance3, AbortToken);
        acquired.Should().BeTrue();
    }

    [Fact]
    public async Task should_handle_concurrent_lock_acquisition()
    {
        // given
        const string key = "publish_retry_v1";
        var ttl = TimeSpan.FromMinutes(5);
        var instances = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid().ToString()).ToArray();

        // Reset lock first
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(AbortToken);
        await connection.ExecuteAsync(
            "UPDATE messaging.lock SET \"Instance\"='', \"LastLockTime\"='0001-01-01 00:00:00'"
        );

        // when - all instances try to acquire simultaneously
        var tasks = instances.Select(instance => _storage.AcquireLockAsync(key, ttl, instance, AbortToken).AsTask());
        var results = await Task.WhenAll(tasks);

        // then - exactly one should succeed
        results.Count(r => r).Should().Be(1);
    }
}
