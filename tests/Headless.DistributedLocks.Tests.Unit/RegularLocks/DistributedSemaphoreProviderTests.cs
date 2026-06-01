// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Tests.Fakes;

namespace Tests.RegularLocks;

public sealed class DistributedSemaphoreProviderTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly FakeDistributedSemaphoreStorage _storage;
    private readonly ILongIdGenerator _longIdGenerator = Substitute.For<ILongIdGenerator>();

    public DistributedSemaphoreProviderTests()
    {
        _storage = new FakeDistributedSemaphoreStorage(_timeProvider);
    }

    [Fact]
    public void should_throw_when_max_count_is_less_than_one()
    {
        // given
        var provider = _CreateProvider();

        // when
        var act = () => provider.CreateSemaphore(Faker.Random.AlphaNumeric(10), 0);

        // then
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("maxCount");
    }

    [Fact]
    public async Task should_allow_up_to_max_count_concurrent_holders()
    {
        // given
        var provider = _CreateProvider();
        var semaphore = provider.CreateSemaphore(Faker.Random.AlphaNumeric(10), maxCount: 2);
        var options = new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero };

        // when
        await using var first = await semaphore.TryAcquireAsync(options, AbortToken);
        await using var second = await semaphore.TryAcquireAsync(options, AbortToken);
        var third = await semaphore.TryAcquireAsync(options, AbortToken);

        // then
        first.Should().NotBeNull();
        second.Should().NotBeNull();
        third.Should().BeNull();
    }

    [Fact]
    public async Task should_reacquire_after_release_and_advance_fencing_token()
    {
        // given
        var provider = _CreateProvider();
        var semaphore = provider.CreateSemaphore(Faker.Random.AlphaNumeric(10), maxCount: 1);
        var options = new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero };

        // when
        await using var first = await semaphore.AcquireAsync(options, AbortToken);
        await first.ReleaseAsync();
        await using var second = await semaphore.AcquireAsync(options, AbortToken);

        // then
        first.FencingToken.Should().Be(1);
        second.FencingToken.Should().Be(2);
    }

    [Fact]
    public async Task should_report_holder_count()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        var semaphore = provider.CreateSemaphore(resource, maxCount: 2);

        // when
        await using var first = await semaphore.AcquireAsync(cancellationToken: AbortToken);
        await using var second = await semaphore.AcquireAsync(cancellationToken: AbortToken);
        var count = await provider.GetHolderCountAsync(resource, AbortToken);

        // then
        count.Should().Be(2);
        first.Should().NotBeNull();
        second.Should().NotBeNull();
    }

    [Fact]
    public async Task should_reacquire_after_expiry()
    {
        // given
        var provider = _CreateProvider();
        var semaphore = provider.CreateSemaphore(Faker.Random.AlphaNumeric(10), maxCount: 1);
        var options = new DistributedLockAcquireOptions
        {
            AcquireTimeout = TimeSpan.Zero,
            TimeUntilExpires = TimeSpan.FromSeconds(10),
        };
        await using var first = await semaphore.AcquireAsync(options, AbortToken);

        // when
        _timeProvider.Advance(TimeSpan.FromSeconds(11));
        await using var second = await semaphore.TryAcquireAsync(options, AbortToken);

        // then
        first.Should().NotBeNull();
        second.Should().NotBeNull();
    }

    [Fact]
    public async Task should_renew_slot_without_changing_fencing_token()
    {
        // given
        var provider = _CreateProvider();
        var semaphore = provider.CreateSemaphore(Faker.Random.AlphaNumeric(10), maxCount: 1);
        await using var slot = await semaphore.AcquireAsync(cancellationToken: AbortToken);

        // when
        var renewed = await slot.RenewAsync(TimeSpan.FromMinutes(5), AbortToken);

        // then
        renewed.Should().BeTrue();
        slot.FencingToken.Should().Be(1);
    }

    private DistributedSemaphoreProvider _CreateProvider(DistributedLockOptions? options = null)
    {
        var counter = 1000L;
        _longIdGenerator.Create().Returns(_ => Interlocked.Increment(ref counter));

        return new DistributedSemaphoreProvider(
            _storage,
            Substitute.For<IOutboxBus>(),
            options ?? new DistributedLockOptions(),
            _longIdGenerator,
            _timeProvider,
            LoggerFactory.CreateLogger<DistributedSemaphoreProvider>()
        );
    }
}
