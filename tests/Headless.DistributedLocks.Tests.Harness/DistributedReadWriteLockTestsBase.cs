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
    /// Polls <paramref name="predicate"/> on the wall clock until it returns <see langword="true"/> or the deadline elapses.
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

        writer.RenewalCount.Should().BePositive();
        (await provider.IsWriteLockedAsync(resource, AbortToken)).Should().BeTrue();
    }

    public virtual async Task should_acquire_composite_read_write_set_in_canonical_order_and_collapse_modes()
    {
        var provider = GetReaderWriterLockProvider();
        var (first, second) = CompositeTestResources.CreatePair();

        // Supplied out of order, and `first` is asked for in both modes. Canonicalization sorts by resource and
        // collapses the pair to a single Write child, because a write lock subsumes a read lock.
        var handle = await provider.AcquireAllAsync(
            [
                new DistributedReadWriteLockRequest(second, DistributedLockMode.Read),
                new DistributedReadWriteLockRequest(first, DistributedLockMode.Write),
                new DistributedReadWriteLockRequest(first, DistributedLockMode.Read),
            ],
            cancellationToken: AbortToken
        );

        try
        {
            handle.Resource.Should().Be($"w:{first}+r:{second}");
            (await provider.IsWriteLockedAsync(first, AbortToken)).Should().BeTrue();
            (await provider.IsReadLockedAsync(second, AbortToken)).Should().BeTrue();
        }
        finally
        {
            await handle.DisposeAsync();
        }

        (await provider.IsWriteLockedAsync(first, AbortToken)).Should().BeFalse();
        (await provider.IsReadLockedAsync(second, AbortToken)).Should().BeFalse();
    }

    /// <summary>
    /// The end-to-end SC1 proof, and the reason the mixed <c>(resource, mode)</c> set exists at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This test only means something because both composites are launched <em>concurrently</em> and each releases as
    /// soon as it is whole. A test where one caller acquires its entire set before the other starts proves nothing —
    /// the second caller simply blocks on the first resource, which happens with or without canonical ordering.
    /// </para>
    /// <para>
    /// X asks for <c>{read first, write second}</c>; Y asks for <c>{read second, write first}</c>. Y's input order is
    /// the reverse of canonical. Strip the ordinal sort and Y acquires <c>read(second)</c> first, so the interleave
    /// where X holds <c>read(first)</c> waiting on <c>write(second)</c> while Y holds <c>read(second)</c> waiting on
    /// <c>write(first)</c> becomes reachable — a circular wait, and both callers time out to <see langword="null"/>.
    /// With canonical ordering both sort to first → second, one wins the contended resource, finishes, releases, and
    /// the other follows. Every iteration must therefore see BOTH composites succeed.
    /// </para>
    /// <para>
    /// This is a probabilistic guard — a lucky scheduler could serialize the two callers and hide a regression in any
    /// single run, which is why the race repeats. The <em>deterministic</em> ordering guard lives in the unit tests,
    /// where acquire-call order is directly observable against a mock. Both are required; neither suffices alone.
    /// </para>
    /// </remarks>
    public virtual async Task should_not_deadlock_when_two_callers_request_opposite_mixed_orders_concurrently()
    {
        var provider = GetReaderWriterLockProvider();

        for (var iteration = 0; iteration < 20; iteration++)
        {
            var (first, second) = CompositeTestResources.CreatePair();
            var options = new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(5) };

            // Both callers are released from one barrier onto separate pool threads. Without this the composites do
            // not actually race: an in-process provider's acquire can complete without ever yielding, so simply
            // calling the async method would run caller X to completion before caller Y was even constructed — and a
            // serialized pair cannot form a circular wait, which would make this test pass with the ordinal sort
            // deleted. It has to genuinely interleave to have any teeth.
            using var startLine = new SemaphoreSlim(0, 2);
            using var callerSource = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
            var callerToken = callerSource.Token;

            var callerX = Task.Run(
                async () =>
                {
                    await startLine.WaitAsync(callerToken);

                    return await _AcquireThenReleaseAsync(
                        provider,
                        [
                            new DistributedReadWriteLockRequest(first, DistributedLockMode.Read),
                            new DistributedReadWriteLockRequest(second, DistributedLockMode.Write),
                        ],
                        options,
                        callerToken
                    );
                },
                callerToken
            );

            var callerY = Task.Run(
                async () =>
                {
                    await startLine.WaitAsync(callerToken);

                    return await _AcquireThenReleaseAsync(
                        provider,
                        [
                            new DistributedReadWriteLockRequest(second, DistributedLockMode.Read),
                            new DistributedReadWriteLockRequest(first, DistributedLockMode.Write),
                        ],
                        options,
                        callerToken
                    );
                },
                callerToken
            );

            startLine.Release(2);

            var race = Task.WhenAll(callerX, callerY);
            bool acquiredX;
            bool acquiredY;

            try
            {
                // The blocked caller re-probes on the provider's TimeProvider-driven backoff, so advance the clock
                // (fake or wall) until both settle. The loop exits the moment the race completes, so a wall-clock leaf
                // pays at most one real delay. The bounded WaitAsync means a genuine deadlock FAILS rather than hangs.
                for (var i = 0; i < 60 && !race.IsCompleted; i++)
                {
                    await AdvanceTimeAsync(TimeSpan.FromMilliseconds(200), AbortToken);
                    await Task.Yield();
                }

                await race.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

                acquiredX = await callerX;
                acquiredY = await callerY;
            }
            finally
            {
                // On a genuine ordering regression the two callers deadlock and the WaitAsync above throws while they
                // are still running against a real backend. Cancel and drain them: an abandoned caller would go on to
                // acquire and hold real locks past the end of this test, turning one honest failure into a cascade of
                // unrelated ones, and its fault would surface later as an unobserved task exception.
                await callerSource.CancelAsync();
                await _DrainCancelledRaceAsync(race);
            }

            acquiredX.Should().BeTrue($"caller X must form its composite on iteration {iteration}");
            acquiredY.Should().BeTrue($"caller Y must form its composite on iteration {iteration}");
        }
    }

    public virtual async Task should_release_earlier_composite_children_when_later_resource_is_contended()
    {
        var provider = GetReaderWriterLockProvider();
        var (first, second) = CompositeTestResources.CreatePair();

        // A blocker holds the ordinally-LATER resource, so the composite acquires `first` and then fails on `second`.
        await using var blocker = await provider.AcquireWriteLockAsync(second, cancellationToken: AbortToken);

        var handle = await provider.TryAcquireAllAsync(
            [
                new DistributedReadWriteLockRequest(second, DistributedLockMode.Write),
                new DistributedReadWriteLockRequest(first, DistributedLockMode.Write),
            ],
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );

        // Rollback proved by observable effect: `first` was genuinely acquired, then released again.
        handle.Should().BeNull();
        (await provider.IsWriteLockedAsync(first, AbortToken)).Should().BeFalse();
        (await provider.IsWriteLockedAsync(second, AbortToken)).Should().BeTrue();
    }

    public virtual async Task should_renew_and_release_composite_read_write_lease()
    {
        var provider = GetReaderWriterLockProvider();
        var (first, second) = CompositeTestResources.CreatePair();

        var handle = await provider.AcquireAllAsync(
            [
                new DistributedReadWriteLockRequest(first, DistributedLockMode.Read),
                new DistributedReadWriteLockRequest(second, DistributedLockMode.Write),
            ],
            cancellationToken: AbortToken
        );

        try
        {
            (await handle.RenewAsync(TimeSpan.FromSeconds(30), AbortToken)).Should().BeTrue();
            await handle.ReleaseAsync();

            (await provider.IsReadLockedAsync(first, AbortToken)).Should().BeFalse();
            (await provider.IsWriteLockedAsync(second, AbortToken)).Should().BeFalse();
        }
        finally
        {
            await handle.DisposeAsync();
        }
    }

    public virtual async Task should_keep_composite_read_write_resources_when_disposed_without_release()
    {
        var provider = GetReaderWriterLockProvider();
        var (first, second) = CompositeTestResources.CreatePair();

        var handle = await provider.AcquireAllAsync(
            [
                new DistributedReadWriteLockRequest(first, DistributedLockMode.Read),
                new DistributedReadWriteLockRequest(second, DistributedLockMode.Write),
            ],
            new DistributedLockAcquireOptions { ReleaseOnDispose = false },
            AbortToken
        );

        await handle.DisposeAsync();

        (await provider.IsReadLockedAsync(first, AbortToken)).Should().BeTrue();
        (await provider.IsWriteLockedAsync(second, AbortToken)).Should().BeTrue();

        await handle.ReleaseAsync();

        (await provider.IsReadLockedAsync(first, AbortToken)).Should().BeFalse();
        (await provider.IsWriteLockedAsync(second, AbortToken)).Should().BeFalse();
    }

    public virtual async Task should_return_child_lease_for_single_canonical_read_write_resource()
    {
        var provider = GetReaderWriterLockProvider();
        var (first, _) = CompositeTestResources.CreatePair();

        // Both entries name one resource, so the canonical set has a single item and there is no composite to build:
        // the provider's own lease is returned, keeping its real LeaseId and its bare resource name.
        var handle = await provider.AcquireAllAsync(
            [
                new DistributedReadWriteLockRequest(first, DistributedLockMode.Read),
                new DistributedReadWriteLockRequest(first, DistributedLockMode.Write),
            ],
            cancellationToken: AbortToken
        );

        try
        {
            handle.Resource.Should().Be(first);
            handle.LeaseId.Should().NotBeNullOrEmpty();
            (await provider.IsWriteLockedAsync(first, AbortToken)).Should().BeTrue();
        }
        finally
        {
            await handle.DisposeAsync();
        }

        (await provider.IsWriteLockedAsync(first, AbortToken)).Should().BeFalse();
    }

    /// <summary>
    /// Acquires the whole set and releases it immediately, so a concurrently-racing caller can make progress. Returns
    /// whether the composite was formed.
    /// </summary>
    private static async Task<bool> _AcquireThenReleaseAsync(
        IDistributedReadWriteLock provider,
        IReadOnlyList<DistributedReadWriteLockRequest> requests,
        DistributedLockAcquireOptions options,
        CancellationToken cancellationToken
    )
    {
        var handle = await provider.TryAcquireAllAsync(requests, options, cancellationToken);

        if (handle is null)
        {
            return false;
        }

        await handle.DisposeAsync();

        return true;
    }

    /// <summary>
    /// Awaits the cancelled race so both callers are settled before the test leaves the iteration, swallowing the
    /// cancellation and any acquire fault they report on the way down. Only reached when the race has already failed,
    /// so there is no outcome left to assert — the point is to leave no caller running.
    /// </summary>
#pragma warning disable CA1031 // The race has already failed; draining it must not mask that failure with a new one.
    private static async Task _DrainCancelledRaceAsync(Task race)
    {
        try
        {
            await race;
        }
        catch (Exception) { }
    }
#pragma warning restore CA1031
}
