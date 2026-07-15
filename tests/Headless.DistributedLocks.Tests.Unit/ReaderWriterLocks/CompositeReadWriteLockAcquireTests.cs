// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;
using Tests.Fakes;

namespace Tests.ReaderWriterLocks;

public sealed class CompositeReadWriteLockAcquireTests : TestBase
{
    [Fact]
    public async Task should_reject_null_request_sequence_before_calling_provider()
    {
        var provider = _CreateProvider(new FakeTimeProvider());

        var act = async () => await provider.TryAcquireAllAsync(null!, cancellationToken: AbortToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
        await _AssertNoAcquireAsync(provider);
    }

    [Fact]
    public async Task should_reject_null_resource_sequence_in_sugar_overloads_before_calling_provider()
    {
        var provider = _CreateProvider(new FakeTimeProvider());

        var readAct = async () => await provider.TryAcquireAllReadAsync(null!, cancellationToken: AbortToken);
        var writeAct = async () => await provider.TryAcquireAllWriteAsync(null!, cancellationToken: AbortToken);

        await readAct.Should().ThrowAsync<ArgumentNullException>();
        await writeAct.Should().ThrowAsync<ArgumentNullException>();
        await _AssertNoAcquireAsync(provider);
    }

    [Fact]
    public async Task should_validate_complete_request_set_before_calling_provider()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        IEnumerable<DistributedReadWriteLockRequest>[] invalidSets =
        [
            [],
            [null!],
            [new(null!, DistributedLockMode.Read)],
            [new("", DistributedLockMode.Read)],
            [new("  ", DistributedLockMode.Write)],
            [new("a", default)], // the None sentinel: default(DistributedLockMode) is never a valid request.
            [new("a", (DistributedLockMode)99)],
            [new("a", DistributedLockMode.Read), new("b", DistributedLockMode.None)],
        ];

        foreach (var invalidSet in invalidSets)
        {
            var act = async () => await provider.TryAcquireAllAsync(invalidSet, cancellationToken: AbortToken);
            await act.Should().ThrowAsync<ArgumentException>();
        }

        await _AssertNoAcquireAsync(provider);
    }

    [Fact]
    public async Task should_enumerate_once_and_acquire_distinct_resources_in_ordinal_order()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var calls = _RecordSuccessfulAcquires(provider);
        var enumerationCount = 0;

        IEnumerable<DistributedReadWriteLockRequest> Requests()
        {
            enumerationCount++;
            yield return new("B", DistributedLockMode.Write);
            yield return new("A", DistributedLockMode.Read);
            yield return new("B", DistributedLockMode.Write);
        }

        var result = await provider.TryAcquireAllAsync(Requests(), cancellationToken: AbortToken);

        result.Should().NotBeNull();
        enumerationCount.Should().Be(1);
        calls.Should().Equal("r:A", "w:B");
    }

    [Fact]
    public async Task should_collapse_a_resource_requested_as_both_read_and_write_into_a_single_write()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var calls = _RecordSuccessfulAcquires(provider);

        var result = await provider.TryAcquireAllAsync(
            [
                new("a", DistributedLockMode.Read),
                new("b", DistributedLockMode.Read),
                new("a", DistributedLockMode.Write),
            ],
            cancellationToken: AbortToken
        );

        result.Should().NotBeNull();
        calls.Should().Equal("w:a", "r:b");
        await provider
            .DidNotReceive()
            .TryAcquireReadLockAsync("a", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_collapse_a_resource_requested_as_write_then_read_into_a_single_write()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var calls = _RecordSuccessfulAcquires(provider);

        var result = await provider.TryAcquireAllAsync(
            [
                new("a", DistributedLockMode.Write),
                new("a", DistributedLockMode.Read),
                new("a", DistributedLockMode.Write),
            ],
            cancellationToken: AbortToken
        );

        result.Should().NotBeNull();
        calls.Should().Equal("w:a");
    }

    /// <summary>
    /// The deterministic deadlock-freedom guard (SC1). Two composites whose inputs disagree on both order and mode
    /// must both begin at the ordinally-first resource. Remove the canonical sort and the first composite starts at
    /// "b" instead, which is the state that lets two callers hold each other's next resource.
    /// </summary>
    [Fact]
    public async Task should_start_opposite_order_opposite_mode_composites_at_the_ordinally_first_resource()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var calls = _RecordSuccessfulAcquires(provider);

        var firstResult = await provider.TryAcquireAllAsync(
            [new("b", DistributedLockMode.Read), new("a", DistributedLockMode.Write)],
            cancellationToken: AbortToken
        );
        var firstCalls = calls.ToArray();
        calls.Clear();

        var secondResult = await provider.TryAcquireAllAsync(
            [new("a", DistributedLockMode.Read), new("b", DistributedLockMode.Write)],
            cancellationToken: AbortToken
        );
        var secondCalls = calls.ToArray();

        firstResult.Should().NotBeNull();
        secondResult.Should().NotBeNull();
        firstCalls.Should().Equal("w:a", "r:b");
        secondCalls.Should().Equal("r:a", "w:b");
        firstCalls[0].Should().EndWith(":a");
        secondCalls[0].Should().EndWith(":a");
    }

    /// <summary>
    /// The interleaved counterpart of the guard above (SC1). Both composites are held mid-formation, so the deadlock
    /// state — each holding the other's next resource — would be reachable if either could take "b" before "a".
    /// Canonical ordering makes it unreachable: the second caller blocks on "a" without ever touching "b".
    /// </summary>
    [Fact]
    public async Task should_make_the_interleaved_formation_deadlock_state_unreachable()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var calls = new ConcurrentQueue<string>();
        var firstWantsB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseB = new TaskCompletionSource<IDistributedLease?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondWantsA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseA = new TaskCompletionSource<IDistributedLease?>(TaskCreationOptions.RunContinuationsAsynchronously);

        provider
            .TryAcquireWriteLockAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var resource = call.ArgAt<string>(0);
                calls.Enqueue("w:" + resource);
                return Task.FromResult<IDistributedLease?>(new CompositeTestLease(resource));
            });

        provider
            .TryAcquireReadLockAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var resource = call.ArgAt<string>(0);
                calls.Enqueue("r:" + resource);

                if (string.Equals(resource, "a", StringComparison.Ordinal))
                {
                    // The second caller's read of "a" contends with the write lock the first caller already holds.
                    secondWantsA.TrySetResult();
                    return releaseA.Task;
                }

                firstWantsB.TrySetResult();
                return releaseB.Task;
            });

        var options = new DistributedLockAcquireOptions { AcquireTimeout = Timeout.InfiniteTimeSpan };
        var firstTask = provider.TryAcquireAllAsync(
            [new("b", DistributedLockMode.Read), new("a", DistributedLockMode.Write)],
            options,
            AbortToken
        );
        await firstWantsB.Task.WaitAsync(AbortToken);

        var secondTask = provider.TryAcquireAllAsync(
            [new("a", DistributedLockMode.Read), new("b", DistributedLockMode.Write)],
            options,
            AbortToken
        );
        await secondWantsA.Task.WaitAsync(AbortToken);

        // The second caller is blocked on "a" and has never asked for "b" -- so it cannot be holding the resource the
        // first caller is waiting for. That is the whole guarantee.
        await provider
            .DidNotReceive()
            .TryAcquireWriteLockAsync("b", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>());
        calls.Should().Equal("w:a", "r:b", "r:a");

        await using var leaseB = new CompositeTestLease("b");
        releaseB.SetResult(leaseB);
        (await firstTask).Should().NotBeNull();
        await using var leaseA = new CompositeTestLease("a");
        releaseA.SetResult(leaseA);
        (await secondTask).Should().NotBeNull();
    }

    [Fact]
    public async Task should_dispatch_each_canonical_entry_to_the_provider_method_for_its_mode()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        _RecordSuccessfulAcquires(provider);

        var result = await provider.TryAcquireAllAsync(
            [new("b", DistributedLockMode.Write), new("a", DistributedLockMode.Read)],
            cancellationToken: AbortToken
        );

        result.Should().NotBeNull();
        await provider
            .Received(1)
            .TryAcquireReadLockAsync("a", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>());
        await provider
            .Received(1)
            .TryAcquireWriteLockAsync("b", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>());
        await provider
            .DidNotReceive()
            .TryAcquireWriteLockAsync("a", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>());
        await provider
            .DidNotReceive()
            .TryAcquireReadLockAsync("b", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_return_original_child_for_single_canonical_request()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
#pragma warning disable CA2000 // Ownership transfers to the composite returned by the acquisition.
        var child = new CompositeTestLease("a", fencingToken: 42);
#pragma warning restore CA2000

        provider
            .TryAcquireWriteLockAsync("a", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(child));

        var result = await provider.TryAcquireAllAsync(
            [new("a", DistributedLockMode.Write), new("a", DistributedLockMode.Read)],
            cancellationToken: AbortToken
        );

        result.Should().BeSameAs(child);
        result!.LeaseId.Should().Be(child.LeaseId);
        result.FencingToken.Should().Be(42);
        result.Resource.Should().Be("a");
        await provider
            .Received(1)
            .TryAcquireWriteLockAsync("a", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_encode_each_mode_in_the_composite_identity()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        _RecordSuccessfulAcquires(provider);

        var mixed = await provider.TryAcquireAllAsync(
            [new("b", DistributedLockMode.Write), new("a", DistributedLockMode.Read)],
            cancellationToken: AbortToken
        );
        var reads = await provider.TryAcquireAllReadAsync(["b", "a"], cancellationToken: AbortToken);

        // A read set and a write set over the same names must not render as the same composite identity.
        mixed.Should().NotBeNull();
        mixed!.Resource.Should().Be("r:a+w:b");
        mixed.FencingToken.Should().BeNull();
        reads.Should().NotBeNull();
        reads!.Resource.Should().Be("r:a+r:b");
    }

    [Fact]
    public async Task should_share_one_budget_across_child_acquisitions()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = _CreateProvider(timeProvider);
        var observedTimeouts = new List<TimeSpan?>();

        provider
            .TryAcquireReadLockAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                observedTimeouts.Add(call.ArgAt<DistributedLockAcquireOptions>(1).AcquireTimeout);
                timeProvider.Advance(TimeSpan.FromSeconds(3));
                return Task.FromResult<IDistributedLease?>(new CompositeTestLease(call.ArgAt<string>(0)));
            });

        provider
            .TryAcquireWriteLockAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                observedTimeouts.Add(call.ArgAt<DistributedLockAcquireOptions>(1).AcquireTimeout);
                return Task.FromResult<IDistributedLease?>(new CompositeTestLease(call.ArgAt<string>(0)));
            });

        var result = await provider.TryAcquireAllAsync(
            [new("a", DistributedLockMode.Read), new("b", DistributedLockMode.Write)],
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(10) },
            AbortToken
        );

        result.Should().NotBeNull();
        observedTimeouts.Should().Equal(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task should_give_every_canonical_child_one_zero_timeout_attempt()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var attempts = new List<(string Entry, TimeSpan? Timeout)>();

        provider
            .TryAcquireReadLockAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var resource = call.ArgAt<string>(0);
                attempts.Add(("r:" + resource, call.ArgAt<DistributedLockAcquireOptions>(1).AcquireTimeout));
                return Task.FromResult<IDistributedLease?>(new CompositeTestLease(resource));
            });

        provider
            .TryAcquireWriteLockAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var resource = call.ArgAt<string>(0);
                attempts.Add(("w:" + resource, call.ArgAt<DistributedLockAcquireOptions>(1).AcquireTimeout));
                return Task.FromResult<IDistributedLease?>(new CompositeTestLease(resource));
            });

        var result = await provider.TryAcquireAllAsync(
            [
                new("b", DistributedLockMode.Write),
                new("a", DistributedLockMode.Read),
                new("b", DistributedLockMode.Write),
            ],
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );

        result.Should().NotBeNull();
        attempts.Select(static attempt => attempt.Entry).Should().Equal("r:a", "w:b");
        attempts.Select(static attempt => attempt.Timeout).Should().OnlyContain(timeout => timeout == TimeSpan.Zero);
    }

    [Fact]
    public async Task should_release_and_dispose_held_children_in_reverse_when_later_acquire_fails()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var events = new List<string>();
#pragma warning disable CA2000 // Ownership transfers to the composite, which releases and disposes it on failure.
        var first = new CompositeTestLease("a", events);
#pragma warning restore CA2000
        provider
            .TryAcquireReadLockAsync("a", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(first));
        provider
            .TryAcquireWriteLockAsync("b", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(null));

        var result = await provider.TryAcquireAllAsync(
            [new("a", DistributedLockMode.Read), new("b", DistributedLockMode.Write)],
            cancellationToken: AbortToken
        );

        result.Should().BeNull();
        events.Should().Equal("release:a", "dispose:a");
    }

    [Fact]
    public async Task should_surface_cleanup_failure_instead_of_returning_null()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var cleanupError = new InvalidOperationException("release failed");
#pragma warning disable CA2000 // Ownership transfers to the composite returned by the acquisition.
        var first = new CompositeTestLease("a", releaseException: cleanupError);
#pragma warning restore CA2000
        provider
            .TryAcquireReadLockAsync("a", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(first));
        provider
            .TryAcquireWriteLockAsync("b", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(null));

        var act = async () =>
            await provider.TryAcquireAllAsync(
                [new("a", DistributedLockMode.Read), new("b", DistributedLockMode.Write)],
                cancellationToken: AbortToken
            );

        var assertion = await act.Should().ThrowAsync<LockCleanupFailedException>();
        assertion.Which.Failures.Should().ContainSingle().Which.Should().BeSameAs(cleanupError);
        assertion.Which.Should().BeAssignableTo<DistributedLockException>();
    }

    [Fact]
    public async Task should_aggregate_primary_acquire_fault_before_cleanup_fault()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var primary = new InvalidOperationException("acquire failed");
        var cleanup = new IOException("release failed");
#pragma warning disable CA2000 // Ownership transfers to the composite returned by the acquisition.
        var first = new CompositeTestLease("a", releaseException: cleanup);
#pragma warning restore CA2000

        provider
            .TryAcquireReadLockAsync("a", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(first));
        provider
            .TryAcquireWriteLockAsync("b", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IDistributedLease?>(primary));

        var act = async () =>
            await provider.TryAcquireAllAsync(
                [new("a", DistributedLockMode.Read), new("b", DistributedLockMode.Write)],
                cancellationToken: AbortToken
            );

        var exception = (await act.Should().ThrowAsync<AggregateException>()).Which;
        exception.InnerExceptions.Should().Equal(primary, cleanup);
    }

    [Fact]
    public async Task should_renew_held_read_child_at_half_ttl_while_later_write_acquire_is_pending()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = _CreateProvider(timeProvider);
#pragma warning disable CA2000 // Ownership transfers to the composite returned by the acquisition.
        var first = new CompositeTestLease("a");
#pragma warning restore CA2000
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResult = new TaskCompletionSource<IDistributedLease?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        provider
            .TryAcquireReadLockAsync("a", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(first));
        provider
            .TryAcquireWriteLockAsync("b", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => _BlockUntilAsync(secondStarted, secondResult.Task));

        var acquireTask = provider.TryAcquireAllAsync(
            [new("b", DistributedLockMode.Write), new("a", DistributedLockMode.Read)],
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(10),
                AcquireTimeout = Timeout.InfiniteTimeSpan,
            },
            AbortToken
        );

        await secondStarted.Task.WaitAsync(AbortToken);
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        await CompositeTestScheduler.DrainUntilAsync(() => first.RenewalCount == 1);
#pragma warning disable CA2000 // Ownership transfers to the composite returned by the acquisition.
        secondResult.SetResult(new CompositeTestLease("b"));
#pragma warning restore CA2000

        (await acquireTask).Should().NotBeNull();
        first.RenewalCount.Should().Be(1);
    }

    /// <summary>
    /// An infinite TTL is not honoured for read children: the provider clamps them to its default so a crashed reader
    /// cannot strand the resource. The composite must schedule formation renewal on that clamped TTL — treating the
    /// set as non-expiring would let the read child expire underneath a long formation and return a lease the caller
    /// does not really hold. The cadence is half the 20-minute default, capped at the coordinator's one-minute ceiling.
    /// </summary>
    [Fact]
    public async Task should_renew_read_children_when_infinite_ttl_is_clamped_by_the_provider()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = _CreateProvider(timeProvider);
        var renewedWith = new ConcurrentQueue<TimeSpan?>();
#pragma warning disable CA2000 // Ownership transfers to the composite returned by the acquisition.
        var first = new CompositeTestLease(
            "a",
            renewal: (timeUntilExpires, _) =>
            {
                renewedWith.Enqueue(timeUntilExpires);
                return Task.FromResult(true);
            }
        );
#pragma warning restore CA2000
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResult = new TaskCompletionSource<IDistributedLease?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        provider
            .TryAcquireReadLockAsync("a", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(first));
        provider
            .TryAcquireWriteLockAsync("b", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => _BlockUntilAsync(secondStarted, secondResult.Task));

        var acquireTask = provider.TryAcquireAllAsync(
            [new("b", DistributedLockMode.Write), new("a", DistributedLockMode.Read)],
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = Timeout.InfiniteTimeSpan,
                AcquireTimeout = Timeout.InfiniteTimeSpan,
            },
            AbortToken
        );

        await secondStarted.Task.WaitAsync(AbortToken);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        await CompositeTestScheduler.DrainUntilAsync(() => first.RenewalCount == 1);
#pragma warning disable CA2000 // Ownership transfers to the composite returned by the acquisition.
        secondResult.SetResult(new CompositeTestLease("b"));
#pragma warning restore CA2000

        (await acquireTask).Should().NotBeNull();
        first.RenewalCount.Should().Be(1);

        // The child is renewed with the REQUESTED TTL and re-applies its own clamp, so a write child in the same set
        // keeps the infinite lease the caller asked for.
        renewedWith.Should().Equal(Timeout.InfiniteTimeSpan);
    }

    [Fact]
    public async Task should_abort_and_report_lost_handle_when_mid_flight_renewal_returns_false()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = _CreateProvider(timeProvider);
        var events = new List<string>();
#pragma warning disable CA2000 // Ownership transfers to the composite returned by the acquisition.
        var first = new CompositeTestLease("a", events, renewResult: false);
#pragma warning restore CA2000
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        provider
            .TryAcquireReadLockAsync("a", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(first));
        provider
            .TryAcquireWriteLockAsync("b", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call => _ReturnNullAfterCancellationAsync(secondStarted, call.ArgAt<CancellationToken>(2)));

        var acquireTask = provider.TryAcquireAllAsync(
            [new("a", DistributedLockMode.Read), new("b", DistributedLockMode.Write)],
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(10),
                AcquireTimeout = Timeout.InfiniteTimeSpan,
            },
            AbortToken
        );

        await secondStarted.Task.WaitAsync(AbortToken);
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        var act = async () => await acquireTask;

        await act.Should().ThrowAsync<LockHandleLostException>().Where(exception => exception.Resource == "a");
        first.RenewalCount.Should().Be(1);
        events.Should().Equal("release:a", "dispose:a");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_report_canonical_resource_on_failure_when_acquire_all(bool tryOnce)
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        provider
            .TryAcquireReadLockAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLease?>(null));
        provider
            .TryAcquireWriteLockAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<IDistributedLease?>(null));
        var options = new DistributedLockAcquireOptions
        {
            AcquireTimeout = tryOnce ? TimeSpan.Zero : TimeSpan.FromSeconds(10),
        };

        var act = async () =>
            await provider.AcquireAllAsync(
                [new("b", DistributedLockMode.Write), new("a", DistributedLockMode.Read)],
                options,
                AbortToken
            );

        var exception = (await act.Should().ThrowAsync<LockAcquisitionTimeoutException>()).Which;
        exception.Resource.Should().Be("r:a+w:b");

        if (tryOnce)
        {
            exception.Message.Should().Contain("first attempt");
        }
    }

    [Fact]
    public async Task should_link_read_and_write_child_loss_tokens_into_one_composite_loss_signal()
    {
        // D4 carries loss linking over from the mutex composite, but nothing exercised it across MIXED modes: a read
        // child and a write child must agree on loss-observability and fold into a single signal.
        var provider = _CreateProvider(new FakeTimeProvider());
#pragma warning disable CA2000 // Ownership transfers to the composite returned by the acquisition.
        var readChild = new CompositeTestLease("a", canObserveLoss: true);
        var writeChild = new CompositeTestLease("b", canObserveLoss: true);
#pragma warning restore CA2000

        provider
            .TryAcquireReadLockAsync("a", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(readChild));
        provider
            .TryAcquireWriteLockAsync("b", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(writeChild));

        await using var handle = await provider.AcquireAllAsync(
            [new("a", DistributedLockMode.Read), new("b", DistributedLockMode.Write)],
            new DistributedLockAcquireOptions
            {
                Monitoring = LockMonitoringMode.Monitor,
                TimeUntilExpires = TimeSpan.FromSeconds(30),
            },
            AbortToken
        );

        handle.CanObserveLoss.Should().BeTrue();
        handle.IsLost.Should().BeFalse();

        // Losing EITHER child loses the whole set — a half-held composite is not a composite.
        writeChild.MarkLost();

        handle.IsLost.Should().BeTrue();
        handle.LostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_reject_read_and_write_children_that_disagree_on_loss_observability()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
#pragma warning disable CA2000 // Ownership transfers to the composite, including its rejection cleanup path.
        var readChild = new CompositeTestLease("a");
        var writeChild = new CompositeTestLease("b", canObserveLoss: true);
#pragma warning restore CA2000

        provider
            .TryAcquireReadLockAsync("a", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(readChild));
        provider
            .TryAcquireWriteLockAsync("b", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(writeChild));

        var act = async () =>
            await provider.TryAcquireAllAsync(
                [new("a", DistributedLockMode.Read), new("b", DistributedLockMode.Write)],
                cancellationToken: AbortToken
            );

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task should_report_the_bare_resource_when_a_single_resource_set_fails_to_acquire()
    {
        // A canonical set of one is not a composite: it is a passthrough, and it must identify itself by its bare
        // resource name on EVERY path. Reporting the mode-encoded identity ("r:a") on the failure path while the
        // success path returns "a" would leak a synthetic name -- one that exists in no backend -- into the timeout
        // exception of what the caller issued as a single-resource acquire.
        var provider = _CreateProvider(new FakeTimeProvider());

        provider
            .TryAcquireReadLockAsync("a", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(null));

        var act = async () =>
            await provider.AcquireAllAsync(
                [new("a", DistributedLockMode.Read)],
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
                AbortToken
            );

        var exception = (await act.Should().ThrowAsync<LockAcquisitionTimeoutException>()).Which;

        exception.Resource.Should().Be("a");
    }

    [Fact]
    public async Task should_produce_the_same_canonical_order_as_the_equivalent_mixed_set_when_read_sugar()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var calls = _RecordSuccessfulAcquires(provider);

        var sugar = await provider.AcquireAllReadAsync(["b", "a"], cancellationToken: AbortToken);
        var sugarCalls = calls.ToArray();
        calls.Clear();

        var mixed = await provider.AcquireAllAsync(
            [new("b", DistributedLockMode.Read), new("a", DistributedLockMode.Read)],
            cancellationToken: AbortToken
        );

        sugarCalls.Should().Equal("r:a", "r:b");
        calls.Should().Equal(sugarCalls);
        sugar.Resource.Should().Be("r:a+r:b");
        mixed.Resource.Should().Be(sugar.Resource);
    }

    [Fact]
    public async Task should_acquire_every_distinct_resource_in_ordinal_order_when_write_sugar()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var calls = _RecordSuccessfulAcquires(provider);

        var result = await provider.AcquireAllWriteAsync(["b", "a", "b"], cancellationToken: AbortToken);

        calls.Should().Equal("w:a", "w:b");
        result.Resource.Should().Be("w:a+w:b");
    }

    private static IDistributedReadWriteLock _CreateProvider(FakeTimeProvider timeProvider)
    {
        var provider = Substitute.For<IDistributedReadWriteLock>();
        provider.TimeProvider.Returns(timeProvider);
        provider.DefaultAcquireTimeout.Returns(TimeSpan.FromSeconds(30));
        provider.DefaultTimeUntilExpires.Returns(TimeSpan.FromMinutes(20));
        return provider;
    }

    /// <summary>
    /// Makes every child acquire succeed and records the acquire order as mode-prefixed entries, so both which
    /// provider method was chosen and in what order are directly observable.
    /// </summary>
    private static List<string> _RecordSuccessfulAcquires(IDistributedReadWriteLock provider)
    {
        var calls = new List<string>();

        provider
            .TryAcquireReadLockAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var resource = call.ArgAt<string>(0);
                calls.Add("r:" + resource);
                return Task.FromResult<IDistributedLease?>(new CompositeTestLease(resource));
            });

        provider
            .TryAcquireWriteLockAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var resource = call.ArgAt<string>(0);
                calls.Add("w:" + resource);
                return Task.FromResult<IDistributedLease?>(new CompositeTestLease(resource));
            });

        return calls;
    }

    private static async Task _AssertNoAcquireAsync(IDistributedReadWriteLock provider)
    {
        await provider
            .DidNotReceive()
            .TryAcquireReadLockAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions>(),
                Arg.Any<CancellationToken>()
            );
        await provider
            .DidNotReceive()
            .TryAcquireWriteLockAsync(
                Arg.Any<string>(),
                Arg.Any<DistributedLockAcquireOptions>(),
                Arg.Any<CancellationToken>()
            );
    }

    private static async Task<IDistributedLease?> _BlockUntilAsync(
        TaskCompletionSource started,
        Task<IDistributedLease?> result
    )
    {
        await Task.Yield();
        started.SetResult();
        return await result.ConfigureAwait(false);
    }

    private static async Task<IDistributedLease?> _ReturnNullAfterCancellationAsync(
        TaskCompletionSource started,
        CancellationToken cancellationToken
    )
    {
        await Task.Yield();
        started.SetResult();

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }

        return null;
    }
}
