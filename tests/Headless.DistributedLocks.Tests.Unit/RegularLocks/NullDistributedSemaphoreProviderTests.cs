// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;

namespace Tests.RegularLocks;

public sealed class NullDistributedSemaphoreProviderTests
{
    [Fact]
    public async Task should_reject_infinite_time_until_expires()
    {
        // given
        var provider = new NullDistributedSemaphoreProvider(TimeProvider.System);
        var semaphore = provider.CreateSemaphore("test.resource", maxCount: 1);

        // when
        var act = async () =>
            await semaphore.TryAcquireAsync(
                new DistributedLockAcquireOptions { TimeUntilExpires = Timeout.InfiniteTimeSpan }
            );

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("options");
    }

    [Fact]
    public void create_semaphore_should_validate_arguments()
    {
        // given
        var provider = new NullDistributedSemaphoreProvider(TimeProvider.System);

        // when / then — the sentinel keeps the same argument contract as real providers
        var emptyResource = () => provider.CreateSemaphore("  ", maxCount: 1);
        emptyResource.Should().Throw<ArgumentException>().WithParameterName("resource");

        var zeroCount = () => provider.CreateSemaphore("resource", maxCount: 0);
        zeroCount.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("maxCount");
    }

    [Fact]
    public async Task should_grant_slot_immediately_with_unmonitored_lease()
    {
        // given
        var provider = new NullDistributedSemaphoreProvider(TimeProvider.System);
        var semaphore = provider.CreateSemaphore("test.resource", maxCount: 2);

        // when
        await using var slot = await semaphore.AcquireAsync(cancellationToken: TestContext.Current.CancellationToken);
        var trySlot = await semaphore.TryAcquireAsync(cancellationToken: TestContext.Current.CancellationToken);

        // then — never contends, exposes the semaphore identity, cannot observe loss
        semaphore.Resource.Should().Be("test.resource");
        semaphore.MaxCount.Should().Be(2);
        slot.Resource.Should().Be("test.resource");
        slot.LeaseId.Should().NotBeNullOrEmpty();
        slot.FencingToken.Should().BeNull();
        slot.CanObserveLoss.Should().BeFalse();
        trySlot.Should().NotBeNull();

        await trySlot!.DisposeAsync();
    }

    [Fact]
    public async Task slot_renew_should_succeed_and_increment_renewal_count()
    {
        // given
        var provider = new NullDistributedSemaphoreProvider(TimeProvider.System);
        var semaphore = provider.CreateSemaphore("test.resource", maxCount: 1);
        await using var slot = await semaphore.AcquireAsync(cancellationToken: TestContext.Current.CancellationToken);
        slot.RenewalCount.Should().Be(0);

        // when
        var renewed = await slot.RenewAsync(cancellationToken: TestContext.Current.CancellationToken);

        // then
        renewed.Should().BeTrue();
        slot.RenewalCount.Should().Be(1);
    }

    [Fact]
    public async Task holder_count_should_always_be_zero()
    {
        // given — the sentinel stores nothing, so observability reads are empty even while held
        var provider = new NullDistributedSemaphoreProvider(TimeProvider.System);
        var semaphore = provider.CreateSemaphore("test.resource", maxCount: 1);
        await using var slot = await semaphore.AcquireAsync(cancellationToken: TestContext.Current.CancellationToken);

        // when
        var count = await provider.GetHolderCountAsync("test.resource", TestContext.Current.CancellationToken);

        // then
        count.Should().Be(0);
    }

    [Fact]
    public async Task should_honor_already_cancelled_token()
    {
        // given
        var provider = new NullDistributedSemaphoreProvider(TimeProvider.System);
        var semaphore = provider.CreateSemaphore("test.resource", maxCount: 1);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await semaphore.AcquireAsync(cancellationToken: cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
