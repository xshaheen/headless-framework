// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests.ReaderWriterLocks;

public sealed class NullDistributedReadWriteLockTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    private NullDistributedReadWriteLock _CreateProvider() => new(_timeProvider);

    [Fact]
    public void should_expose_injected_time_provider()
    {
        var provider = _CreateProvider();

        provider.TimeProvider.Should().BeSameAs(_timeProvider);
    }

    [Fact]
    public void should_expose_null_logger_when_none_is_injected()
    {
        // given / when — composite coordination reads Logger off the provider, so it can never be null
        var provider = _CreateProvider();

        // then
        provider.Logger.Should().NotBeNull();
    }

    [Fact]
    public async Task should_acquire_read_lock_immediately()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        await using var lease = await provider.AcquireReadLockAsync(resource, cancellationToken: AbortToken);

        // then — the null provider never contends and reports no loss-observation capability
        lease.Resource.Should().Be(resource);
        lease.LeaseId.Should().NotBeNullOrEmpty();
        lease.FencingToken.Should().BeNull();
        lease.CanObserveLoss.Should().BeFalse();
        lease.DateAcquired.Should().Be(_timeProvider.GetUtcNow());
    }

    [Fact]
    public async Task should_acquire_write_lock_immediately()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        await using var lease = await provider.AcquireWriteLockAsync(resource, cancellationToken: AbortToken);

        // then
        lease.Resource.Should().Be(resource);
        lease.LeaseId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task try_acquire_should_never_return_null()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var readLease = await provider.TryAcquireReadLockAsync(resource, cancellationToken: AbortToken);
        var writeLease = await provider.TryAcquireWriteLockAsync(resource, cancellationToken: AbortToken);

        // then
        readLease.Should().NotBeNull();
        writeLease.Should().NotBeNull();

        await readLease!.DisposeAsync();
        await writeLease!.DisposeAsync();
    }

    [Fact]
    public async Task should_report_unlocked_and_zero_readers()
    {
        // given — even while a lease is held, the null provider reports no contention state
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        await using var lease = await provider.AcquireWriteLockAsync(resource, cancellationToken: AbortToken);

        // when / then
        (await provider.IsReadLockedAsync(resource, AbortToken))
            .Should()
            .BeFalse();
        (await provider.IsWriteLockedAsync(resource, AbortToken)).Should().BeFalse();
        (await provider.GetReaderCountAsync(resource, AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task renew_should_succeed_and_increment_renewal_count()
    {
        // given
        var provider = _CreateProvider();
        await using var lease = await provider.AcquireReadLockAsync(
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
    public async Task should_reject_monitoring_with_infinite_lease()
    {
        // given — the same contract as the real providers: lease monitoring needs a finite TTL
        var provider = _CreateProvider();
        var options = new DistributedLockAcquireOptions
        {
            Monitoring = LockMonitoringMode.Monitor,
            TimeUntilExpires = Timeout.InfiniteTimeSpan,
        };

        // when
        var act = async () => await provider.AcquireWriteLockAsync(Faker.Random.AlphaNumeric(10), options, AbortToken);

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
        var act = async () =>
            await provider.AcquireReadLockAsync(Faker.Random.AlphaNumeric(10), cancellationToken: cts.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
