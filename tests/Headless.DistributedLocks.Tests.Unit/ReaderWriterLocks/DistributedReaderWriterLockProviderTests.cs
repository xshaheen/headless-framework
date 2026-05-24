// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Tests.Fakes;

namespace Tests.ReaderWriterLocks;

public sealed class DistributedReaderWriterLockProviderTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly ILongIdGenerator _longIdGenerator = Substitute.For<ILongIdGenerator>();
    private long _lockIdCounter = 1000;

    private DistributedReaderWriterLockProvider _CreateProvider(
        IDistributedReaderWriterLockStorage? storage = null,
        IOutboxPublisher? outboxPublisher = null,
        DistributedLockOptions? options = null
    )
    {
        _longIdGenerator.Create().Returns(_ => Interlocked.Increment(ref _lockIdCounter));

        return new DistributedReaderWriterLockProvider(
            storage ?? new FakeReaderWriterLockStorage(),
            outboxPublisher,
            options ?? new DistributedLockOptions(),
            _longIdGenerator,
            _timeProvider,
            LoggerFactory.CreateLogger<DistributedReaderWriterLockProvider>()
        );
    }

    [Fact]
    public async Task should_acquire_multiple_readers_for_same_resource()
    {
        // given
        var storage = new FakeReaderWriterLockStorage();
        var provider = _CreateProvider(storage);
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        await using var first = await provider.AcquireReadLockAsync(resource, cancellationToken: AbortToken);
        await using var second = await provider.AcquireReadLockAsync(resource, cancellationToken: AbortToken);

        // then
        first.Resource.Should().Be(resource);
        second.Resource.Should().Be(resource);
        (await provider.GetReaderCountAsync(resource, AbortToken)).Should().Be(2);
    }

    [Fact]
    public async Task should_dispatch_read_renew_and_release_to_read_storage_methods()
    {
        // given
        var storage = Substitute.For<IDistributedReaderWriterLockStorage>();
        storage
            .TryAcquireReadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(true);
        storage
            .TryExtendReadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var provider = _CreateProvider(storage);
        var resource = Faker.Random.AlphaNumeric(10);
        var scopedResource = $"distributed-lock:{resource}";

        // when
        await using var handle = await provider.AcquireReadLockAsync(resource, cancellationToken: AbortToken);
        var renewed = await handle.RenewAsync(cancellationToken: AbortToken);
        await handle.ReleaseAsync();

        // then
        renewed.Should().BeTrue();
        await storage.Received(1).TryExtendReadAsync(scopedResource, handle.LockId, Arg.Any<TimeSpan?>(), AbortToken);
        await storage.Received(1).ReleaseReadAsync(scopedResource, handle.LockId, Arg.Any<CancellationToken>());
        await storage
            .DidNotReceive()
            .TryExtendWriteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_dispatch_write_renew_and_release_to_write_storage_methods()
    {
        // given
        var storage = Substitute.For<IDistributedReaderWriterLockStorage>();
        storage
            .TryAcquireWriteAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<TimeSpan?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);
        storage
            .TryExtendWriteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var provider = _CreateProvider(storage);
        var resource = Faker.Random.AlphaNumeric(10);
        var scopedResource = $"distributed-lock:{resource}";

        // when
        await using var handle = await provider.AcquireWriteLockAsync(resource, cancellationToken: AbortToken);
        var renewed = await handle.RenewAsync(cancellationToken: AbortToken);
        await handle.ReleaseAsync();

        // then
        renewed.Should().BeTrue();
        await storage.Received(1).TryExtendWriteAsync(scopedResource, handle.LockId, Arg.Any<TimeSpan?>(), AbortToken);
        await storage.Received(1).ReleaseWriteAsync(scopedResource, handle.LockId, Arg.Any<CancellationToken>());
        await storage
            .DidNotReceive()
            .TryExtendReadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_cleanup_writer_waiting_marker_when_try_write_times_out()
    {
        // given
        var storage = new FakeReaderWriterLockStorage();
        var provider = _CreateProvider(storage);
        var resource = Faker.Random.AlphaNumeric(10);
        storage.SetRead($"distributed-lock:{resource}", "reader-1");

        // when
        var result = await provider.TryAcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );

        // then
        result.Should().BeNull();
        storage.WriteReleaseCount.Should().Be(1);
        (await provider.IsWriteLockedAsync(resource, AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_return_null_when_non_zero_write_acquire_timeout_elapses()
    {
        // given
        var storage = new FakeReaderWriterLockStorage();
        var provider = _CreateProvider(storage);
        var resource = Faker.Random.AlphaNumeric(10);
        storage.SetRead($"distributed-lock:{resource}", "reader-1");

        // when
        var acquireTask = provider.TryAcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(1) },
            AbortToken
        );
        await Task.Yield();
        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        var result = await acquireTask;

        // then
        result.Should().BeNull();
        storage.WriteReleaseCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task should_throw_when_monitoring_uses_infinite_lease()
    {
        // given
        var provider = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var act = async () =>
            await provider.AcquireReadLockAsync(
                resource,
                new DistributedLockAcquireOptions
                {
                    TimeUntilExpires = Timeout.InfiniteTimeSpan,
                    Monitoring = LockMonitoringMode.Monitor,
                },
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
