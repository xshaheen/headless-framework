// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;

namespace Tests;

public abstract class DistributedReadWriteLockTestsBase : TestBase
{
    protected abstract IDistributedReadWriteLock GetReaderWriterLockProvider(DistributedLockOptions? options = null);
    protected abstract TimeProvider TimeProvider { get; }
    protected abstract Task AdvanceTimeAsync(TimeSpan amount, CancellationToken cancellationToken);

    protected static async Task DrainUntilAsync(Func<bool> condition, CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < 2000 && !condition(); i++)
        {
            if (i % 100 is 0)
            {
                await TimeProvider.System.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);
            }
            else
            {
                await Task.Yield();
            }
        }
    }

    /// <summary>
    /// Polls <paramref name="predicate"/> on the wall clock until it returns <c>true</c> or the deadline elapses.
    /// Used by wall-clock leaves (Redis) to absorb scheduling jitter when waiting for an asynchronous effect
    /// (e.g. the writer-waiting marker becoming visible). On fake-clock leaves (InMemory) the predicate already
    /// holds because <see cref="AdvanceTimeAsync"/> drains the thread pool, so it converges on the first probe.
    /// </summary>
    protected static async Task<bool> EventuallyAsync(
        Func<Task<bool>> predicate,
        TimeSpan? deadline = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default
    )
    {
        var effectiveDeadline = deadline ?? TimeSpan.FromSeconds(5);
        var effectivePollInterval = pollInterval ?? TimeSpan.FromMilliseconds(25);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        while (true)
        {
            if (await predicate())
            {
                return true;
            }

            if (sw.Elapsed >= effectiveDeadline)
            {
                return false;
            }

            await TimeProvider.System.Delay(effectivePollInterval, cancellationToken);
        }
    }

    protected virtual Task WaitForWriterQueuedAsync(string resource, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public virtual async Task should_allow_multiple_readers_and_release_on_dispose()
    {
        var provider = GetReaderWriterLockProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        await using (var first = await provider.AcquireReadLockAsync(resource, cancellationToken: AbortToken))
        await using (var second = await provider.AcquireReadLockAsync(resource, cancellationToken: AbortToken))
        {
            first.LeaseId.Should().NotBe(second.LeaseId);
            (await provider.GetReaderCountAsync(resource, AbortToken)).Should().Be(2);
        }

        (await provider.IsReadLockedAsync(resource, AbortToken)).Should().BeFalse();
    }

    public virtual async Task should_acquire_write_lock_exclusively()
    {
        var provider = GetReaderWriterLockProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        await using var writer1 = await provider.AcquireWriteLockAsync(resource, cancellationToken: AbortToken);

        var writer2 = await provider.TryAcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );
        var reader = await provider.TryAcquireReadLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );

        writer2.Should().BeNull();
        reader.Should().BeNull();

        await writer1.ReleaseAsync();

        await using var writer3 = await provider.TryAcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );
        writer3.Should().NotBeNull();
    }

    public virtual async Task should_release_read_lock_and_allow_writer()
    {
        var provider = GetReaderWriterLockProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        var reader = await provider.AcquireReadLockAsync(resource, cancellationToken: AbortToken);
        var writer1 = await provider.TryAcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );
        writer1.Should().BeNull();

        await reader.ReleaseAsync();

        await using var writer2 = await provider.TryAcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );
        writer2.Should().NotBeNull();
    }

    public virtual async Task should_queue_second_writer_and_unblock_after_first_releases()
    {
        var provider = GetReaderWriterLockProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        var firstWriter = await provider.AcquireWriteLockAsync(resource, cancellationToken: AbortToken);

        var secondWriterTask = provider.AcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(5) },
            AbortToken
        );
        await Task.Delay(TimeSpan.FromMilliseconds(200), AbortToken);
        secondWriterTask.IsCompleted.Should().BeFalse();

        await firstWriter.ReleaseAsync();

        // The queued writer re-probes on the provider's TimeProvider-driven backoff, so advance the clock
        // (fake or wall) until it observes the release; the wall-clock world simply waits real time.
        for (var i = 0; i < 20 && !secondWriterTask.IsCompleted; i++)
        {
            await AdvanceTimeAsync(TimeSpan.FromMilliseconds(200), AbortToken);
            await Task.Yield();
        }

        await using var secondWriter = await secondWriterTask.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        secondWriter.Should().NotBeNull();
        (await provider.IsWriteLockedAsync(resource, AbortToken)).Should().BeTrue();
    }

    public virtual async Task should_leave_lock_held_when_release_on_dispose_is_false()
    {
        var provider = GetReaderWriterLockProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        var handle = await provider.AcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions { ReleaseOnDispose = false },
            AbortToken
        );

        await handle.DisposeAsync();

        (await provider.IsWriteLockedAsync(resource, AbortToken)).Should().BeTrue();

        await handle.ReleaseAsync();
        (await provider.IsWriteLockedAsync(resource, AbortToken)).Should().BeFalse();
    }

    public virtual async Task should_be_idempotent_for_stale_release()
    {
        var provider = GetReaderWriterLockProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        var handle = await provider.AcquireWriteLockAsync(resource, cancellationToken: AbortToken);
        await handle.ReleaseAsync();

        var act = async () => await handle.ReleaseAsync();

        await act.Should().NotThrowAsync();
    }

    public virtual async Task should_throw_when_acquire_read_blocked_by_writer()
    {
        var provider = GetReaderWriterLockProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        var writer = await provider.AcquireWriteLockAsync(resource, cancellationToken: AbortToken);

        try
        {
            // The acquire-timeout is driven by the provider's TimeProvider, so the fake-clock world must be advanced
            // past it for the timeout to fire; the wall-clock world fires on its own and absorbs the extra advance.
            var acquireTask = provider.AcquireReadLockAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromMilliseconds(200) },
                AbortToken
            );

            var act = () => _AdvancePastAcquireTimeoutAsync(acquireTask, TimeSpan.FromMilliseconds(200));

            await act.Should().ThrowAsync<LockAcquisitionTimeoutException>();
        }
        finally
        {
            await writer.DisposeAsync();
        }
    }

    public virtual async Task should_throw_when_acquire_write_blocked_by_reader()
    {
        var provider = GetReaderWriterLockProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        var reader = await provider.AcquireReadLockAsync(resource, cancellationToken: AbortToken);

        try
        {
            var acquireTask = provider.AcquireWriteLockAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromMilliseconds(500) },
                AbortToken
            );

            var act = () => _AdvancePastAcquireTimeoutAsync(acquireTask, TimeSpan.FromMilliseconds(500));

            await act.Should().ThrowAsync<LockAcquisitionTimeoutException>();
        }
        finally
        {
            await reader.DisposeAsync();
        }
    }

    private async Task _AdvancePastAcquireTimeoutAsync(Task<IDistributedLease> acquireTask, TimeSpan acquireTimeout)
    {
        for (var i = 0; i < 20 && !acquireTask.IsCompleted; i++)
        {
            await AdvanceTimeAsync(acquireTimeout, AbortToken);
            await Task.Yield();
        }

        await acquireTask;
    }

    public virtual async Task should_prefer_queued_writer_over_new_reader()
    {
        var provider = GetReaderWriterLockProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        await using var reader = await provider.AcquireReadLockAsync(resource, cancellationToken: AbortToken);

        var writerTask = provider.TryAcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(20) },
            AbortToken
        );

        await WaitForWriterQueuedAsync(resource, AbortToken);

        var blockedReader = await provider.TryAcquireReadLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );
        await reader.ReleaseAsync();

        for (var i = 0; i < 20 && !writerTask.IsCompleted; i++)
        {
            await AdvanceTimeAsync(TimeSpan.FromSeconds(1), AbortToken);
            await Task.Yield();
        }

        await using var writer = await writerTask;

        blockedReader.Should().BeNull();
        writer.Should().NotBeNull();
        (await provider.IsWriteLockedAsync(resource, AbortToken)).Should().BeTrue();
    }

    public virtual async Task should_clear_writer_waiting_marker_when_try_acquire_write_times_out()
    {
        var provider = GetReaderWriterLockProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        await using var reader = await provider.AcquireReadLockAsync(resource, cancellationToken: AbortToken);

        var result = await provider.TryAcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );

        result.Should().BeNull();
        (await provider.IsWriteLockedAsync(resource, AbortToken)).Should().BeFalse();
        (await provider.GetReaderCountAsync(resource, AbortToken)).Should().Be(1);

        var secondReader = await provider.TryAcquireReadLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );
        secondReader.Should().NotBeNull();
        await secondReader!.DisposeAsync();
    }

    public virtual async Task should_clear_writer_waiting_marker_when_try_acquire_write_is_cancelled()
    {
        var provider = GetReaderWriterLockProvider();
        var resource = Faker.Random.AlphaNumeric(10);
        await using var reader = await provider.AcquireReadLockAsync(resource, cancellationToken: AbortToken);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(300));

        var act = async () =>
            await provider.TryAcquireWriteLockAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(5) },
                cts.Token
            );

        await act.Should().ThrowAsync<OperationCanceledException>();

        var secondReader = await provider.TryAcquireReadLockAsync(
            resource,
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );
        secondReader.Should().NotBeNull();
        await secondReader!.DisposeAsync();
    }

    public virtual async Task should_fire_handle_lost_token_when_read_lock_ttl_expires()
    {
        var provider = GetReaderWriterLockProvider(new DistributedLockOptions { PollingCadenceFraction = 0.1 });
        var resource = Faker.Random.AlphaNumeric(10);
        await using var handle = await provider.AcquireReadLockAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(1),
                Monitoring = LockMonitoringMode.Monitor,
            },
            AbortToken
        );

        for (var i = 0; i < 10 && !handle.LostToken.IsCancellationRequested; i++)
        {
            await AdvanceTimeAsync(TimeSpan.FromSeconds(1), AbortToken);
            await DrainUntilAsync(() => handle.LostToken.IsCancellationRequested, AbortToken);
        }

        handle.LostToken.IsCancellationRequested.Should().BeTrue();
    }

    public virtual async Task should_fire_handle_lost_token_when_write_lock_ttl_expires()
    {
        var provider = GetReaderWriterLockProvider(new DistributedLockOptions { PollingCadenceFraction = 0.1 });
        var resource = Faker.Random.AlphaNumeric(10);
        await using var handle = await provider.AcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(1),
                Monitoring = LockMonitoringMode.Monitor,
            },
            AbortToken
        );

        for (var i = 0; i < 10 && !handle.LostToken.IsCancellationRequested; i++)
        {
            await AdvanceTimeAsync(TimeSpan.FromSeconds(1), AbortToken);
            await DrainUntilAsync(() => handle.LostToken.IsCancellationRequested, AbortToken);
        }

        handle.LostToken.IsCancellationRequested.Should().BeTrue();
    }

    public virtual async Task should_auto_extend_write_lock()
    {
        var provider = GetReaderWriterLockProvider(
            new DistributedLockOptions { AutoExtensionCadenceFraction = 0.1, PollingCadenceFraction = 0.1 }
        );
        var resource = Faker.Random.AlphaNumeric(10);

        await using var writer = await provider.AcquireWriteLockAsync(
            resource,
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(1),
                Monitoring = LockMonitoringMode.AutoExtend,
            },
            AbortToken
        );

        await AdvanceTimeAsync(TimeSpan.FromSeconds(3), AbortToken);
        await DrainUntilAsync(() => writer.RenewalCount > 0, AbortToken);

        writer.RenewalCount.Should().BeGreaterThan(0);
        (await provider.IsWriteLockedAsync(resource, AbortToken)).Should().BeTrue();
    }
}
