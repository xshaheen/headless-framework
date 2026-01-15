// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.ResourceLocks;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class InMemoryResourceLockProviderTests : ResourceLockProviderTestsBase
{
    private ServiceProvider? _provider;

    protected override IResourceLockProvider GetLockProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider);
        services.AddSingleton<IGuidGenerator>(GuidGenerator);
        services.AddInMemoryResourceLock();

        _provider = services.BuildServiceProvider();
        return _provider.GetRequiredService<IResourceLockProvider>();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }

        await base.DisposeAsyncCore();
    }

    [Fact]
    public override Task should_lock_with_try_acquire()
    {
        return base.should_lock_with_try_acquire();
    }

    [Fact]
    public override Task should_not_acquire_when_already_locked()
    {
        return base.should_not_acquire_when_already_locked();
    }

    [Fact]
    public override Task should_obtain_multiple_locks()
    {
        return base.should_obtain_multiple_locks();
    }

    [Fact]
    public override async Task should_release_lock_multiple_times()
    {
        await base.should_release_lock_multiple_times();
    }

    [Fact]
    public override Task should_timeout_when_try_to_lock_acquired_resource()
    {
        return base.should_timeout_when_try_to_lock_acquired_resource();
    }

    [Fact]
    public override Task should_lock_one_at_a_time_async()
    {
        return base.should_lock_one_at_a_time_async();
    }

    [Fact]
    public override Task should_acquire_and_release_locks_async()
    {
        return base.should_acquire_and_release_locks_async();
    }

    [Fact]
    public override Task should_acquire_one_at_a_time_parallel()
    {
        return base.should_acquire_one_at_a_time_parallel();
    }

    [Fact]
    public override Task should_acquire_locks_in_sync()
    {
        return base.should_acquire_locks_in_sync();
    }

    [Fact]
    public override Task should_acquire_locks_in_parallel()
    {
        return base.should_acquire_locks_in_parallel();
    }

    [Fact]
    public override Task should_get_expiration_for_locked_resource()
    {
        return base.should_get_expiration_for_locked_resource();
    }

    [Fact]
    public override Task should_return_null_expiration_when_not_locked()
    {
        return base.should_return_null_expiration_when_not_locked();
    }

    [Fact]
    public override Task should_get_lock_info_for_locked_resource()
    {
        return base.should_get_lock_info_for_locked_resource();
    }

    [Fact]
    public override Task should_return_null_lock_info_when_not_locked()
    {
        return base.should_return_null_lock_info_when_not_locked();
    }

    [Fact]
    public override Task should_list_active_locks()
    {
        return base.should_list_active_locks();
    }

    [Fact]
    public override Task should_get_active_locks_count()
    {
        return base.should_get_active_locks_count();
    }
}
