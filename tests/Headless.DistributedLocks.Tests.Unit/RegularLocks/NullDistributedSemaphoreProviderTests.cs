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
}
