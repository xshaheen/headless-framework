// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.DistributedLocks.InMemory;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class InMemoryStorageDeterministicTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();

    [Fact]
    public async Task lock_storage_should_prune_expired_entries_and_keep_monotonic_fencing()
    {
        var storage = new InMemoryDistributedLockStorage(_timeProvider);
        var key = Faker.Random.AlphaNumeric(10);

        var first = await storage.InsertAsync(key, "lock-1", TimeSpan.FromSeconds(5), AbortToken);
        var rejected = await storage.InsertAsync(key, "lock-2", TimeSpan.FromSeconds(5), AbortToken);
        _timeProvider.Advance(TimeSpan.FromSeconds(6));
        var second = await storage.InsertAsync(key, "lock-3", TimeSpan.FromSeconds(5), AbortToken);

        first.FencingToken.Should().Be(1);
        rejected.Acquired.Should().BeFalse();
        rejected.FencingToken.Should().BeNull();
        second.FencingToken.Should().Be(2);
        (await storage.GetAsync(key, AbortToken)).Should().Be("lock-3");
    }

    [Fact]
    public async Task reader_writer_storage_should_expire_readers_and_waiting_marker_deterministically()
    {
        var storage = new InMemoryDistributedReadWriteLockStorage(_timeProvider);
        var resource = Faker.Random.AlphaNumeric(10);
        var writerId = Guid.NewGuid().ToString("N");
        var waitingId = DistributedLockCoreHelpers.GetWriterWaitingId(writerId);

        (await storage.TryAcquireReadAsync(resource, "reader-1", TimeSpan.FromSeconds(5), AbortToken))
            .Should()
            .BeTrue();
        var queued = await storage.TryAcquireWriteAsync(
            resource,
            writerId,
            waitingId,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(2),
            AbortToken
        );
        var blockedReader = await storage.TryAcquireReadAsync(
            resource,
            "reader-2",
            TimeSpan.FromSeconds(5),
            AbortToken
        );

        _timeProvider.Advance(TimeSpan.FromSeconds(3));
        var readerAfterMarkerExpiry = await storage.TryAcquireReadAsync(
            resource,
            "reader-3",
            TimeSpan.FromSeconds(5),
            AbortToken
        );

        _timeProvider.Advance(TimeSpan.FromSeconds(3));

        queued.Should().BeFalse();
        blockedReader.Should().BeFalse();
        readerAfterMarkerExpiry.Should().BeTrue();
        (await storage.GetReaderCountAsync(resource, AbortToken)).Should().Be(1);
    }

    [Fact]
    public async Task reader_writer_storage_should_extend_without_shortening_and_reject_expired_leases()
    {
        var storage = new InMemoryDistributedReadWriteLockStorage(_timeProvider);
        var resource = Faker.Random.AlphaNumeric(10);
        var writerId = Guid.NewGuid().ToString("N");

        (
            await storage.TryAcquireWriteAsync(
                resource,
                writerId,
                DistributedLockCoreHelpers.GetWriterWaitingId(writerId),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(2),
                AbortToken
            )
        )
            .Should()
            .BeTrue();

        var shortExtend = await storage.TryExtendWriteAsync(resource, writerId, TimeSpan.FromSeconds(1), AbortToken);
        _timeProvider.Advance(TimeSpan.FromSeconds(2));
        var stillValid = await storage.ValidateWriteAsync(resource, writerId, AbortToken);
        _timeProvider.Advance(TimeSpan.FromSeconds(9));
        var expiredExtend = await storage.TryExtendWriteAsync(resource, writerId, TimeSpan.FromSeconds(10), AbortToken);

        shortExtend.Should().BeTrue();
        stillValid.Should().BeTrue();
        expiredExtend.Should().BeFalse();
        (await storage.IsWriteLockedAsync(resource, AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task reader_writer_storage_should_refuse_read_extend_when_writer_is_waiting()
    {
        var storage = new InMemoryDistributedReadWriteLockStorage(_timeProvider);
        var resource = Faker.Random.AlphaNumeric(10);
        var writerId = Guid.NewGuid().ToString("N");

        (await storage.TryAcquireReadAsync(resource, "reader-1", TimeSpan.FromSeconds(10), AbortToken))
            .Should()
            .BeTrue();
        (
            await storage.TryAcquireWriteAsync(
                resource,
                writerId,
                DistributedLockCoreHelpers.GetWriterWaitingId(writerId),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(5),
                AbortToken
            )
        )
            .Should()
            .BeFalse();

        var renewed = await storage.TryExtendReadAsync(resource, "reader-1", TimeSpan.FromSeconds(10), AbortToken);

        renewed.Should().BeFalse();
    }

    [Fact]
    public async Task reader_writer_storage_should_not_shorten_infinite_write_lease()
    {
        var storage = new InMemoryDistributedReadWriteLockStorage(_timeProvider);
        var resource = Faker.Random.AlphaNumeric(10);
        var writerId = Guid.NewGuid().ToString("N");

        (
            await storage.TryAcquireWriteAsync(
                resource,
                writerId,
                DistributedLockCoreHelpers.GetWriterWaitingId(writerId),
                ttl: null,
                TimeSpan.FromSeconds(5),
                AbortToken
            )
        )
            .Should()
            .BeTrue();

        var renewed = await storage.TryExtendWriteAsync(resource, writerId, TimeSpan.FromSeconds(1), AbortToken);
        _timeProvider.Advance(TimeSpan.FromSeconds(2));

        renewed.Should().BeTrue();
        (await storage.ValidateWriteAsync(resource, writerId, AbortToken)).Should().BeTrue();
    }

    [Fact]
    public async Task lock_storage_should_keep_fencing_monotonic_under_concurrent_acquire_release()
    {
        // Guards the window where grant and fence-increment were two unsynchronized ConcurrentDictionary
        // ops: a preempted caller could return Acquired=true with a token above the live holder. With the
        // per-resource lock, a holder that actually holds the lock never sees a token below an earlier one.
        var storage = new InMemoryDistributedLockStorage(TimeProvider.System);
        var key = Faker.Random.AlphaNumeric(10);

        const int workers = 16;
        const int iterations = 200;
        var maxToken = 0L;
        var failures = 0;

        async Task contendAsync()
        {
            var leaseId = Guid.NewGuid().ToString("N");

            for (var i = 0; i < iterations; i++)
            {
                var result = await storage.InsertAsync(key, leaseId, TimeSpan.FromSeconds(30), AbortToken);

                if (!result.Acquired)
                {
                    continue;
                }

                var token = result.FencingToken!.Value;
                var observedMax = Interlocked.Read(ref maxToken);

                // The holder owns the lock right now; its token must not be below a token already granted.
                if (token < observedMax)
                {
                    Interlocked.Increment(ref failures);
                }

                _Max(ref maxToken, token);
                await storage.RemoveIfEqualAsync(key, leaseId, AbortToken);
            }
        }

        var tasks = Enumerable.Range(0, workers).Select(_ => Task.Run(contendAsync, AbortToken));
        await Task.WhenAll(tasks);

        failures.Should().Be(0);
        maxToken.Should().BePositive();
    }

    [Fact]
    public async Task reader_writer_storage_should_clear_stale_marker_when_write_claim_succeeds()
    {
        var storage = new InMemoryDistributedReadWriteLockStorage(_timeProvider);
        var resource = Faker.Random.AlphaNumeric(10);
        var writerA = Guid.NewGuid().ToString("N");
        var writerB = Guid.NewGuid().ToString("N");

        // Reader present -> writer A plants a marker but cannot claim.
        (await storage.TryAcquireReadAsync(resource, "reader-1", TimeSpan.FromSeconds(30), AbortToken))
            .Should()
            .BeTrue();
        (
            await storage.TryAcquireWriteAsync(
                resource,
                writerA,
                DistributedLockCoreHelpers.GetWriterWaitingId(writerA),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30),
                AbortToken
            )
        )
            .Should()
            .BeFalse();

        // Reader released -> writer B claims (clearing the stale marker) then releases.
        await storage.ReleaseReadAsync(resource, "reader-1", AbortToken);
        (
            await storage.TryAcquireWriteAsync(
                resource,
                writerB,
                DistributedLockCoreHelpers.GetWriterWaitingId(writerB),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30),
                AbortToken
            )
        )
            .Should()
            .BeTrue();
        await storage.ReleaseWriteAsync(resource, writerB, AbortToken);

        // No stale marker from writer A may block a fresh reader.
        (await storage.TryAcquireReadAsync(resource, "reader-2", TimeSpan.FromSeconds(30), AbortToken))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task reader_writer_storage_should_keep_reader_lease_finite_when_extended_with_null_ttl()
    {
        var storage = new InMemoryDistributedReadWriteLockStorage(_timeProvider);
        var resource = Faker.Random.AlphaNumeric(10);

        (await storage.TryAcquireReadAsync(resource, "reader-1", TimeSpan.FromSeconds(5), AbortToken))
            .Should()
            .BeTrue();

        // A null ttl must NOT promote a reader lease to infinite (zombie-reader hazard).
        (await storage.TryExtendReadAsync(resource, "reader-1", ttl: null, AbortToken))
            .Should()
            .BeTrue();

        _timeProvider.Advance(TimeSpan.FromSeconds(6));

        // The reader stayed finite and is now expired.
        (await storage.ValidateReadAsync(resource, "reader-1", AbortToken))
            .Should()
            .BeFalse();
        (await storage.GetReaderCountAsync(resource, AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task reader_writer_storage_should_keep_writer_lease_infinite_when_extended_with_null_ttl()
    {
        var storage = new InMemoryDistributedReadWriteLockStorage(_timeProvider);
        var resource = Faker.Random.AlphaNumeric(10);
        var writerId = Guid.NewGuid().ToString("N");

        (
            await storage.TryAcquireWriteAsync(
                resource,
                writerId,
                DistributedLockCoreHelpers.GetWriterWaitingId(writerId),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                AbortToken
            )
        )
            .Should()
            .BeTrue();

        // Writers may go infinite: a null ttl extends to a non-expiring lease.
        (await storage.TryExtendWriteAsync(resource, writerId, ttl: null, AbortToken))
            .Should()
            .BeTrue();

        _timeProvider.Advance(TimeSpan.FromSeconds(60));

        (await storage.ValidateWriteAsync(resource, writerId, AbortToken)).Should().BeTrue();
    }

    [Theory]
    [InlineData("lock:with:colon")]
    [InlineData("a:b")]
    public async Task reader_writer_storage_should_reject_lock_id_containing_colon(string leaseId)
    {
        var storage = new InMemoryDistributedReadWriteLockStorage(_timeProvider);
        var resource = Faker.Random.AlphaNumeric(10);

        var acquireRead = async () =>
            await storage.TryAcquireReadAsync(resource, leaseId, TimeSpan.FromSeconds(5), AbortToken);
        var acquireWrite = async () =>
            await storage.TryAcquireWriteAsync(
                resource,
                leaseId,
                DistributedLockCoreHelpers.GetWriterWaitingId(leaseId),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                AbortToken
            );

        await acquireRead.Should().ThrowAsync<InvalidOperationException>();
        await acquireWrite.Should().ThrowAsync<InvalidOperationException>();
    }

    private static void _Max(ref long target, long value)
    {
        long current;

        do
        {
            current = Interlocked.Read(ref target);

            if (value <= current)
            {
                return;
            }
        } while (Interlocked.CompareExchange(ref target, value, current) != current);
    }

    [Fact]
    public void setup_should_register_all_three_providers()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDistributedLocks(setup => setup.UseInMemory());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDistributedLock>().Should().NotBeNull();
        provider.GetRequiredService<IDistributedSemaphoreProvider>().Should().NotBeNull();
        provider.GetRequiredService<IDistributedReadWriteLock>().Should().NotBeNull();
    }
}
