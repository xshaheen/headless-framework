// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.RegularLocks;

public sealed class NullDistributedLockTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    private NullDistributedLock _CreateProvider()
    {
        return new(_timeProvider);
    }

    [Fact]
    public void should_expose_injected_time_provider()
    {
        var provider = _CreateProvider();

        provider.TimeProvider.Should().BeSameAs(_timeProvider);
    }

    [Fact]
    public async Task should_acquire_immediately_with_unmonitored_lease()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        await using var lease = await provider.AcquireAsync(resource, cancellationToken: AbortToken);

        // then — the sentinel provider always grants and cannot observe loss
        lease.Resource.Should().Be(resource);
        lease.LeaseId.Should().NotBeNullOrEmpty();
        lease.FencingToken.Should().BeNull();
        lease.CanObserveLoss.Should().BeFalse();
        lease.LostToken.Should().Be(CancellationToken.None);
        lease.DateAcquired.Should().Be(_timeProvider.GetUtcNow());
        lease.TimeWaitedForLock.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task should_never_return_null_when_try_acquire()
    {
        // given
        var provider = _CreateProvider();

        // when
        var lease = await provider.TryAcquireAsync(Faker.Random.AlphaNumeric(10), cancellationToken: AbortToken);

        // then
        lease.Should().NotBeNull();
        await lease!.DisposeAsync();
    }

    [Fact]
    public async Task should_always_report_success_when_provider_renew()
    {
        // given
        var provider = _CreateProvider();

        // when — even a lease id that was never issued renews successfully
        var renewed = await provider.RenewAsync(
            Faker.Random.AlphaNumeric(10),
            "unknown-lease",
            cancellationToken: AbortToken
        );

        // then
        renewed.Should().BeTrue();
    }

    [Fact]
    public async Task should_succeed_and_increment_renewal_count_when_lease_renew()
    {
        // given
        var provider = _CreateProvider();
        await using var lease = await provider.AcquireAsync(
            Faker.Random.AlphaNumeric(10),
            cancellationToken: AbortToken
        );
        lease.RenewalCount.Should().Be(0);

        // when
        var renewed = await lease.RenewAsync(cancellationToken: AbortToken);

        // then
        renewed.Should().BeTrue();
        lease.RenewalCount.Should().Be(1);
    }

    [Fact]
    public async Task should_report_no_lock_state_even_while_lease_is_held()
    {
        // given — the null provider stores nothing, so all observability reads are empty
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        await using var lease = await provider.AcquireAsync(resource, cancellationToken: AbortToken);

        // when / then
        (await provider.IsLockedAsync(resource, AbortToken))
            .Should()
            .BeFalse();
        (await provider.GetLeaseIdAsync(resource, AbortToken)).Should().BeNull();
        (await provider.GetExpirationAsync(resource, AbortToken)).Should().BeNull();
        (await provider.GetLockInfoAsync(resource, AbortToken)).Should().BeNull();
        (await provider.ListActiveLocksAsync(AbortToken)).Should().BeEmpty();
        (await provider.GetActiveLocksCountAsync(AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task should_be_a_no_op_that_completes_when_release()
    {
        // given
        var provider = _CreateProvider();

        // when
        var act = async () => await provider.ReleaseAsync(Faker.Random.AlphaNumeric(10), "lease-1", AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_reject_monitoring_with_infinite_lease()
    {
        // given — same contract as the real providers: monitoring needs a finite TTL
        var provider = _CreateProvider();
        var options = new DistributedLockAcquireOptions
        {
            Monitoring = LockMonitoringMode.Monitor,
            TimeUntilExpires = Timeout.InfiniteTimeSpan,
        };

        // when
        var act = async () => await provider.AcquireAsync(Faker.Random.AlphaNumeric(10), options, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("options");
    }

    [Fact]
    public async Task should_honor_already_cancelled_token()
    {
        // given
        var provider = _CreateProvider();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // when
        var act = async () => await provider.AcquireAsync(Faker.Random.AlphaNumeric(10), cancellationToken: cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
