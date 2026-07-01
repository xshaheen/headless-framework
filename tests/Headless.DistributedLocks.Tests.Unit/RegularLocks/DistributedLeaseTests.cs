// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;

namespace Tests.RegularLocks;

public sealed class DistributedLeaseTests
{
    [Fact]
    public async Task should_report_not_lost_and_not_throw_when_lost_token_is_not_cancelled()
    {
        // given
        using var lostSource = new CancellationTokenSource();
        await using IDistributedLease lease = new TestLease(lostSource.Token);

        // when
        var act = lease.ThrowIfLost;

        // then
        lease.IsLost.Should().BeFalse();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task should_report_lost_and_throw_when_lost_token_is_cancelled()
    {
        // given
        using var lostSource = new CancellationTokenSource();
        await using IDistributedLease lease = new TestLease(lostSource.Token);

        // when
        await lostSource.CancelAsync();
        var act = lease.ThrowIfLost;

        // then
        lease.IsLost.Should().BeTrue();
        act.Should()
            .Throw<LockHandleLostException>()
            .Where(exception => exception.Resource == lease.Resource && exception.LeaseId == lease.LeaseId);
    }

    private sealed class TestLease(CancellationToken lostToken) : IDistributedLease
    {
        public string LeaseId => "test-lease";

        public long? FencingToken => null;

        public string Resource => "test-resource";

        public int RenewalCount => 0;

        public DateTimeOffset DateAcquired => DateTimeOffset.UnixEpoch;

        public TimeSpan TimeWaitedForLock => TimeSpan.Zero;

        public CancellationToken LostToken { get; } = lostToken;

        public bool CanObserveLoss => LostToken.CanBeCanceled;

        public Task ReleaseAsync() => Task.CompletedTask;

        public Task<bool> RenewAsync(TimeSpan? timeUntilExpires = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
