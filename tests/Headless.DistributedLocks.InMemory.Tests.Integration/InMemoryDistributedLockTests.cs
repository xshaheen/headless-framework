// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class InMemoryDistributedLockTests : DistributedLockTestsBase
{
    protected override IDistributedLock GetLockProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup =>
        {
            setup.ConfigureOptions(static options => options.KeyPrefix = Options.KeyPrefix);
            setup.UseInMemory();
        });

        return services.BuildServiceProvider().GetRequiredService<IDistributedLock>();
    }

    [Fact]
    public override Task should_lock_with_try_acquire() => base.should_lock_with_try_acquire();

    [Fact]
    public override Task should_lock_with_acquire() => base.should_lock_with_acquire();

    [Fact]
    public override Task should_not_acquire_when_already_locked() => base.should_not_acquire_when_already_locked();

    [Fact]
    public override Task should_throw_timeout_with_acquire_when_already_locked() =>
        base.should_throw_timeout_with_acquire_when_already_locked();

    [Fact]
    public override Task should_obtain_multiple_locks() => base.should_obtain_multiple_locks();

    [Fact]
    public override Task should_release_lock_multiple_times() => base.should_release_lock_multiple_times();

    [Fact]
    public override Task should_keep_lock_when_disposed_with_release_on_dispose_false() =>
        base.should_keep_lock_when_disposed_with_release_on_dispose_false();

    [Fact]
    public override Task should_release_explicitly_when_release_on_dispose_false() =>
        base.should_release_explicitly_when_release_on_dispose_false();

    [Fact]
    public override Task should_timeout_when_try_to_lock_acquired_resource() =>
        base.should_timeout_when_try_to_lock_acquired_resource();

    [Fact]
    public override Task should_acquire_and_release_locks_async() => base.should_acquire_and_release_locks_async();

    [Fact]
    public override Task should_acquire_one_at_a_time_parallel() => base.should_acquire_one_at_a_time_parallel();

    [Fact]
    public override Task should_acquire_locks_in_sync() => base.should_acquire_locks_in_sync();

    [Fact]
    public override Task should_acquire_locks_in_parallel() => base.should_acquire_locks_in_parallel();

    [Fact]
    public override Task should_lock_one_at_a_time_async() => base.should_lock_one_at_a_time_async();

    [Fact]
    public override Task should_get_expiration_for_locked_resource() =>
        base.should_get_expiration_for_locked_resource();

    [Fact]
    public override Task should_return_null_expiration_when_not_locked() =>
        base.should_return_null_expiration_when_not_locked();

    [Fact]
    public override Task should_get_lock_info_for_locked_resource() => base.should_get_lock_info_for_locked_resource();

    [Fact]
    public override Task should_return_null_lock_info_when_not_locked() =>
        base.should_return_null_lock_info_when_not_locked();

    [Fact]
    public override Task should_list_active_locks() => base.should_list_active_locks();

    [Fact]
    public override Task should_get_active_locks_count() => base.should_get_active_locks_count();

    [Fact]
    public override Task should_expose_none_handle_lost_token_without_monitoring() =>
        base.should_expose_none_handle_lost_token_without_monitoring();

    [Fact]
    public override Task should_keep_lock_alive_when_auto_extend_is_enabled_smoke() =>
        base.should_keep_lock_alive_when_auto_extend_is_enabled_smoke();
}
