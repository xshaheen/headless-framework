// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;

namespace Tests;

public sealed class NullDistributedLockTests : TestBase
{
    [Fact]
    public async Task should_return_non_null_handle_when_TryAcquireAsync_called()
    {
        // given
        var sut = new NullDistributedLock(TimeProvider.System);

        // when
        var handle = await sut.TryAcquireAsync("test.resource", cancellationToken: AbortToken);

        // then
        handle.Should().NotBeNull();
        handle!.Resource.Should().Be("test.resource");
        handle.LeaseId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task should_succeed_when_RenewAsync_called_on_handle()
    {
        // given
        var sut = new NullDistributedLock(TimeProvider.System);
        var handle = await sut.TryAcquireAsync("test.resource", cancellationToken: AbortToken);
        handle.Should().NotBeNull();

        // when
        var renewed = await handle!.RenewAsync(TimeSpan.FromMinutes(1), AbortToken);

        // then
        renewed.Should().BeTrue();
    }

    [Fact]
    public async Task should_increment_renewal_count_after_each_renew_async_call()
    {
        // given
        var sut = new NullDistributedLock(TimeProvider.System);
        var handle = await sut.TryAcquireAsync("test.resource", cancellationToken: AbortToken);
        handle.Should().NotBeNull();
        handle!.RenewalCount.Should().Be(0);

        // when
        await handle.RenewAsync(TimeSpan.FromMinutes(1), AbortToken);
        await handle.RenewAsync(TimeSpan.FromMinutes(1), AbortToken);
        await handle.RenewAsync(TimeSpan.FromMinutes(1), AbortToken);

        // then
        handle.RenewalCount.Should().Be(3);
    }

    [Fact]
    public async Task should_not_increment_renewal_count_when_RenewAsync_token_is_already_cancelled()
    {
        // given
        var sut = new NullDistributedLock(TimeProvider.System);
        var handle = await sut.TryAcquireAsync("test.resource", cancellationToken: AbortToken);
        handle.Should().NotBeNull();
        handle!.RenewalCount.Should().Be(0);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await handle.RenewAsync(TimeSpan.FromMinutes(1), cts.Token);

        // then — ThrowIfCancellationRequested fires before the Interlocked.Increment
        await act.Should().ThrowAsync<OperationCanceledException>();
        handle.RenewalCount.Should().Be(0);
    }

    [Fact]
    public async Task should_be_safe_to_DisposeAsync_multiple_times()
    {
        // given
        var sut = new NullDistributedLock(TimeProvider.System);
        var handle = await sut.TryAcquireAsync("test.resource", cancellationToken: AbortToken);
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
    public async Task should_throw_OperationCanceledException_when_TryAcquireAsync_token_is_already_cancelled()
    {
        // given
        var sut = new NullDistributedLock(TimeProvider.System);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await sut.TryAcquireAsync("test.resource", cancellationToken: cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_report_handle_as_unmonitored_when_monitoring_is_requested()
    {
        // given
        var sut = new NullDistributedLock(TimeProvider.System);

        // when
        var handle = await sut.TryAcquireAsync(
            "test.resource",
            new DistributedLockAcquireOptions { Monitoring = LockMonitoringMode.Monitor },
            cancellationToken: AbortToken
        );

        // then
        handle.Should().NotBeNull();
        handle!.CanObserveLoss.Should().BeFalse();
        handle.LostToken.Should().Be(CancellationToken.None);
    }

    [Fact]
    public async Task should_reject_infinite_ttl_when_monitoring_is_enabled()
    {
        // given
        var sut = new NullDistributedLock(TimeProvider.System);

        // when
        var act = async () =>
            await sut.TryAcquireAsync(
                "test.resource",
                new DistributedLockAcquireOptions
                {
                    TimeUntilExpires = Timeout.InfiniteTimeSpan,
                    Monitoring = LockMonitoringMode.Monitor,
                },
                cancellationToken: AbortToken
            );

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("options");
    }

    [Fact]
    public async Task should_throw_OperationCanceledException_when_provider_RenewAsync_token_is_already_cancelled()
    {
        // given
        var sut = new NullDistributedLock(TimeProvider.System);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await sut.RenewAsync("test.resource", "lock-id", cancellationToken: cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_throw_OperationCanceledException_when_provider_ReleaseAsync_token_is_already_cancelled()
    {
        // given
        var sut = new NullDistributedLock(TimeProvider.System);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await sut.ReleaseAsync("test.resource", "lock-id", cancellationToken: cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_return_empty_list_when_ListActiveLocksAsync_called()
    {
        // given
        var sut = new NullDistributedLock(TimeProvider.System);
        await sut.TryAcquireAsync("test.resource", cancellationToken: AbortToken);

        // when
        var locks = await sut.ListActiveLocksAsync(AbortToken);

        // then — null provider never tracks active locks (silent introspection contract)
        locks.Should().NotBeNull();
        locks.Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_zero_when_GetActiveLocksCountAsync_called()
    {
        // given
        var sut = new NullDistributedLock(TimeProvider.System);
        await sut.TryAcquireAsync("test.resource", cancellationToken: AbortToken);

        // when
        var count = await sut.GetActiveLocksCountAsync(AbortToken);

        // then — null provider never tracks active locks (silent introspection contract)
        count.Should().Be(0L);
    }
}
