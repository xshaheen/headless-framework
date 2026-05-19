// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;

namespace Tests;

public sealed class NoOpDistributedLockProviderTests
{
    [Fact]
    public async Task should_return_non_null_handle_when_TryAcquireAsync_called()
    {
        // given
        var sut = new NoOpDistributedLockProvider(TimeProvider.System);

        // when
        var handle = await sut.TryAcquireAsync("test.resource");

        // then
        handle.Should().NotBeNull();
        handle!.Resource.Should().Be("test.resource");
        handle.LockId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task should_succeed_when_RenewAsync_called_on_handle()
    {
        // given
        var sut = new NoOpDistributedLockProvider(TimeProvider.System);
        var handle = await sut.TryAcquireAsync("test.resource");
        handle.Should().NotBeNull();

        // when
        var renewed = await handle!.RenewAsync(TimeSpan.FromMinutes(1));

        // then
        renewed.Should().BeTrue();
    }

    [Fact]
    public async Task should_increment_renewal_count_after_each_renew_async_call()
    {
        // given
        var sut = new NoOpDistributedLockProvider(TimeProvider.System);
        var handle = await sut.TryAcquireAsync("test.resource");
        handle.Should().NotBeNull();
        handle!.RenewalCount.Should().Be(0);

        // when
        await handle.RenewAsync(TimeSpan.FromMinutes(1));
        await handle.RenewAsync(TimeSpan.FromMinutes(1));
        await handle.RenewAsync(TimeSpan.FromMinutes(1));

        // then
        handle.RenewalCount.Should().Be(3);
    }

    [Fact]
    public async Task should_be_safe_to_DisposeAsync_multiple_times()
    {
        // given
        var sut = new NoOpDistributedLockProvider(TimeProvider.System);
        var handle = await sut.TryAcquireAsync("test.resource");
        handle.Should().NotBeNull();

        // when / then — disposing repeatedly must not throw
        var act = async () =>
        {
            await handle!.DisposeAsync();
            await handle.DisposeAsync();
            await handle.DisposeAsync();
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_return_empty_list_when_ListActiveLocksAsync_called()
    {
        // given
        var sut = new NoOpDistributedLockProvider(TimeProvider.System);
        await sut.TryAcquireAsync("test.resource");

        // when
        var locks = await sut.ListActiveLocksAsync();

        // then — NoOp never tracks active locks (silent introspection contract)
        locks.Should().NotBeNull();
        locks.Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_zero_when_GetActiveLocksCountAsync_called()
    {
        // given
        var sut = new NoOpDistributedLockProvider(TimeProvider.System);
        await sut.TryAcquireAsync("test.resource");

        // when
        var count = await sut.GetActiveLocksCountAsync();

        // then — NoOp never tracks active locks (silent introspection contract)
        count.Should().Be(0L);
    }
}
