// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;
using NSubstitute.Core;
using Tests.Fakes;

namespace Tests.RegularLocks;

public sealed class CompositeSemaphoreAcquireTests : TestBase
{
    [Fact]
    public async Task should_reject_null_request_sequence_before_creating_any_semaphore()
    {
        var provider = _CreateProvider(new FakeTimeProvider());

        var act = async () => await provider.TryAcquireAllAsync(null!, cancellationToken: AbortToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
        provider.DidNotReceive().CreateSemaphore(Arg.Any<string>(), Arg.Any<int>());
    }

    [Fact]
    public async Task should_validate_complete_request_set_before_creating_any_semaphore()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        IEnumerable<DistributedSemaphoreRequest>[] invalidSets =
        [
            [],
            [null!],
            [new(null!, 1)],
            [new("", 1)],
            [new("  ", 1)],
            [new("a", 5), new("  ", 1)],
        ];

        foreach (var invalidSet in invalidSets)
        {
            var act = async () => await provider.TryAcquireAllAsync(invalidSet, cancellationToken: AbortToken);
            await act.Should().ThrowAsync<ArgumentException>();
        }

        provider.DidNotReceive().CreateSemaphore(Arg.Any<string>(), Arg.Any<int>());
    }

    [Fact]
    public async Task should_reject_non_positive_max_count_before_creating_any_semaphore()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        int[] invalidCounts = [0, -1];

        foreach (var invalidCount in invalidCounts)
        {
            var act = async () =>
                await provider.TryAcquireAllAsync(
                    [new DistributedSemaphoreRequest("a", 5), new DistributedSemaphoreRequest("b", invalidCount)],
                    cancellationToken: AbortToken
                );

            await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        }

        provider.DidNotReceive().CreateSemaphore(Arg.Any<string>(), Arg.Any<int>());
    }

    [Fact]
    public async Task should_reject_conflicting_max_count_for_one_resource_before_creating_any_semaphore()
    {
        var provider = _CreateProvider(new FakeTimeProvider());

        var act = async () =>
            await provider.TryAcquireAllAsync(
                [new DistributedSemaphoreRequest("a", 5), new DistributedSemaphoreRequest("a", 3)],
                cancellationToken: AbortToken
            );

        // maxCount is a property of the semaphore, not of the acquisition: (a, 5) and (a, 3) name two different
        // semaphores that cannot both exist. That is a caller bug, so it fails eagerly -- nothing is created and no
        // slot is taken, which is what makes the failure free of compensating cleanup.
        var exception = (await act.Should().ThrowAsync<ArgumentException>()).Which;
        exception.Message.Should().Contain("'a'");
        provider.DidNotReceive().CreateSemaphore(Arg.Any<string>(), Arg.Any<int>());
    }

    [Fact]
    public async Task should_dedupe_identical_duplicate_requests_into_one_child()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var slot = new CompositeTestLease("a");
        var semaphore = _StubSemaphore(provider, "a", 5, _ => Task.FromResult<IDistributedLease?>(slot));

        var result = await provider.TryAcquireAllAsync(
            [new DistributedSemaphoreRequest("a", 5), new DistributedSemaphoreRequest("a", 5)],
            cancellationToken: AbortToken
        );

        // One canonical child means the single-item passthrough path: CompositeDistributedLease requires two or more
        // children, so a composite could not have been constructed here at all.
        result.Should().BeSameAs(slot);
        provider.Received(1).CreateSemaphore("a", 5);
        await semaphore
            .Received(1)
            .TryAcquireAsync(Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_return_original_slot_lease_for_single_canonical_resource()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var slot = new CompositeTestLease("a", fencingToken: 42);
        _StubSemaphore(provider, "a", 5, _ => Task.FromResult<IDistributedLease?>(slot));

        var result = await provider.TryAcquireAllAsync(
            [new DistributedSemaphoreRequest("a", 5)],
            cancellationToken: AbortToken
        );

        result.Should().BeSameAs(slot);
        result!.LeaseId.Should().Be(slot.LeaseId);
        result.Resource.Should().Be("a");
        result.FencingToken.Should().Be(42);
    }

    [Fact]
    public async Task should_create_differently_sized_semaphores_and_acquire_in_ordinal_order()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var calls = new List<string>();
        var enumerationCount = 0;

        IEnumerable<DistributedSemaphoreRequest> Requests()
        {
            enumerationCount++;
            yield return new DistributedSemaphoreRequest("b", 2);
            yield return new DistributedSemaphoreRequest("a", 5);
        }

        _StubSemaphore(
            provider,
            "a",
            5,
            _ =>
            {
                calls.Add("a");
                return Task.FromResult<IDistributedLease?>(new CompositeTestLease("a"));
            }
        );
        _StubSemaphore(
            provider,
            "b",
            2,
            _ =>
            {
                calls.Add("b");
                return Task.FromResult<IDistributedLease?>(new CompositeTestLease("b"));
            }
        );

        var result = await provider.TryAcquireAllAsync(Requests(), cancellationToken: AbortToken);

        result.Should().NotBeNull();
        enumerationCount.Should().Be(1);
        provider.Received(1).CreateSemaphore("a", 5);
        provider.Received(1).CreateSemaphore("b", 2);
        calls.Should().Equal("a", "b");
    }

    [Fact]
    public async Task should_expose_plain_ordinal_join_and_no_fencing_token_on_the_composite()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        _StubSemaphore(
            provider,
            "a",
            5,
            _ => Task.FromResult<IDistributedLease?>(new CompositeTestLease("a", fencingToken: 7))
        );
        _StubSemaphore(
            provider,
            "b",
            2,
            _ => Task.FromResult<IDistributedLease?>(new CompositeTestLease("b", fencingToken: 9))
        );

        var result = await provider.TryAcquireAllAsync(
            [new DistributedSemaphoreRequest("b", 2), new DistributedSemaphoreRequest("a", 5)],
            cancellationToken: AbortToken
        );

        result.Should().NotBeNull();

        // Capacity is not identity: the composite name is the plain ordinal resource join. It exists in no backend --
        // and neither does a fencing token for a set, even though each individual slot carries one.
        result!.Resource.Should().Be("a+b");
        result.FencingToken.Should().BeNull();
    }

    [Fact]
    public async Task should_share_one_budget_across_slot_acquisitions()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = _CreateProvider(timeProvider);
        var observedTimeouts = new List<TimeSpan?>();

        _StubSemaphore(
            provider,
            "a",
            5,
            call =>
            {
                observedTimeouts.Add(call.ArgAt<DistributedLockAcquireOptions>(0).AcquireTimeout);
                timeProvider.Advance(TimeSpan.FromSeconds(3));
                return Task.FromResult<IDistributedLease?>(new CompositeTestLease("a"));
            }
        );
        _StubSemaphore(
            provider,
            "b",
            2,
            call =>
            {
                observedTimeouts.Add(call.ArgAt<DistributedLockAcquireOptions>(0).AcquireTimeout);
                return Task.FromResult<IDistributedLease?>(new CompositeTestLease("b"));
            }
        );

        var result = await provider.TryAcquireAllAsync(
            [new DistributedSemaphoreRequest("a", 5), new DistributedSemaphoreRequest("b", 2)],
            new DistributedLockAcquireOptions
            {
                AcquireTimeout = TimeSpan.FromSeconds(10),
                TimeUntilExpires = TimeSpan.FromSeconds(30),
            },
            AbortToken
        );

        result.Should().NotBeNull();
        observedTimeouts.Should().Equal(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task should_give_every_canonical_slot_one_zero_timeout_attempt()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var attempts = new List<(string Resource, TimeSpan? Timeout)>();

        _StubSemaphore(
            provider,
            "a",
            5,
            call =>
            {
                attempts.Add(("a", call.ArgAt<DistributedLockAcquireOptions>(0).AcquireTimeout));
                return Task.FromResult<IDistributedLease?>(new CompositeTestLease("a"));
            }
        );
        _StubSemaphore(
            provider,
            "b",
            2,
            call =>
            {
                attempts.Add(("b", call.ArgAt<DistributedLockAcquireOptions>(0).AcquireTimeout));
                return Task.FromResult<IDistributedLease?>(new CompositeTestLease("b"));
            }
        );

        var result = await provider.TryAcquireAllAsync(
            [
                new DistributedSemaphoreRequest("b", 2),
                new DistributedSemaphoreRequest("a", 5),
                new DistributedSemaphoreRequest("b", 2),
            ],
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );

        result.Should().NotBeNull();
        attempts.Select(static attempt => attempt.Resource).Should().Equal("a", "b");
        attempts.Select(static attempt => attempt.Timeout).Should().OnlyContain(timeout => timeout == TimeSpan.Zero);
    }

    [Fact]
    public async Task should_release_and_dispose_held_slot_when_later_slot_is_unavailable()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var events = new List<string>();
        var first = new CompositeTestLease("a", events);
        _StubSemaphore(provider, "a", 5, _ => Task.FromResult<IDistributedLease?>(first));
        _StubSemaphore(provider, "b", 2, _ => Task.FromResult<IDistributedLease?>(null));

        var result = await provider.TryAcquireAllAsync(
            [new DistributedSemaphoreRequest("a", 5), new DistributedSemaphoreRequest("b", 2)],
            cancellationToken: AbortToken
        );

        result.Should().BeNull();
        events.Should().Equal("release:a", "dispose:a");
    }

    [Fact]
    public async Task should_surface_rollback_cleanup_failure_instead_of_returning_null()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var cleanupError = new InvalidOperationException("release failed");
        var first = new CompositeTestLease("a", releaseException: cleanupError);
        _StubSemaphore(provider, "a", 5, _ => Task.FromResult<IDistributedLease?>(first));
        _StubSemaphore(provider, "b", 2, _ => Task.FromResult<IDistributedLease?>(null));

        var act = async () =>
            await provider.TryAcquireAllAsync(
                [new DistributedSemaphoreRequest("a", 5), new DistributedSemaphoreRequest("b", 2)],
                cancellationToken: AbortToken
            );

        // Rollback after a failed acquisition surfaces cleanup errors; only DisposeAsync of a successfully returned
        // composite swallows and logs.
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
        var first = new CompositeTestLease("a", releaseException: cleanup);
        _StubSemaphore(provider, "a", 5, _ => Task.FromResult<IDistributedLease?>(first));
        _StubSemaphore(provider, "b", 2, _ => Task.FromException<IDistributedLease?>(primary));

        var act = async () =>
            await provider.TryAcquireAllAsync(
                [new DistributedSemaphoreRequest("a", 5), new DistributedSemaphoreRequest("b", 2)],
                cancellationToken: AbortToken
            );

        var exception = (await act.Should().ThrowAsync<AggregateException>()).Which;
        exception.InnerExceptions.Should().Equal(primary, cleanup);
    }

    [Fact]
    public async Task should_renew_held_slot_at_half_ttl_while_later_slot_is_pending()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = _CreateProvider(timeProvider);
        var first = new CompositeTestLease("a");
        var second = new CompositeTestLease("b");
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResult = new TaskCompletionSource<IDistributedLease?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        _StubSemaphore(provider, "a", 5, _ => Task.FromResult<IDistributedLease?>(first));
        _StubSemaphore(provider, "b", 2, _ => _BlockSecondAsync(secondStarted, secondResult.Task));

        var acquireTask = provider.TryAcquireAllAsync(
            [new DistributedSemaphoreRequest("a", 5), new DistributedSemaphoreRequest("b", 2)],
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
        secondResult.SetResult(second);

        (await acquireTask).Should().NotBeNull();
        first.RenewalCount.Should().Be(1);
    }

    [Fact]
    public async Task should_surface_semaphore_rejection_of_infinite_ttl_so_formation_renewal_always_applies()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var rejection = new ArgumentException("a slot is stored with a finite expiry score");
        _StubSemaphore(
            provider,
            "a",
            5,
            call =>
                call.ArgAt<DistributedLockAcquireOptions>(0).TimeUntilExpires == Timeout.InfiniteTimeSpan
                    ? Task.FromException<IDistributedLease?>(rejection)
                    : Task.FromResult<IDistributedLease?>(new CompositeTestLease("a"))
        );

        var act = async () =>
            await provider.TryAcquireAllAsync(
                [new DistributedSemaphoreRequest("a", 5)],
                new DistributedLockAcquireOptions { TimeUntilExpires = Timeout.InfiniteTimeSpan },
                AbortToken
            );

        // A semaphore slot carries a finite expiry score, so an infinite TTL never reaches a held slot. The
        // coordinator's infinite-TTL "skip renewal" branch is therefore unreachable for semaphore composites: held
        // slots always have a finite TTL and are always renewed during formation (see the half-TTL test above).
        (await act.Should().ThrowAsync<ArgumentException>())
            .Which.Should()
            .BeSameAs(rejection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task acquire_all_should_report_canonical_resource_on_failure(bool tryOnce)
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        _StubSemaphore(provider, "a", 5, _ => Task.FromResult<IDistributedLease?>(null));
        _StubSemaphore(provider, "b", 2, _ => Task.FromResult<IDistributedLease?>(null));
        var options = new DistributedLockAcquireOptions
        {
            AcquireTimeout = tryOnce ? TimeSpan.Zero : TimeSpan.FromSeconds(10),
        };

        var act = async () =>
            await provider.AcquireAllAsync(
                [new DistributedSemaphoreRequest("b", 2), new DistributedSemaphoreRequest("a", 5)],
                options,
                AbortToken
            );

        var exception = (await act.Should().ThrowAsync<LockAcquisitionTimeoutException>()).Which;
        exception.Resource.Should().Be("a+b");

        if (tryOnce)
        {
            exception.Message.Should().Contain("first attempt");
        }
    }

    [Fact]
    public async Task should_link_slot_loss_tokens_into_one_composite_loss_signal()
    {
        // D4 carries loss linking over from the mutex composite; nothing exercised it for semaphore slots.
        var provider = _CreateProvider(new FakeTimeProvider());
        var firstSlot = new CompositeTestLease("a", canObserveLoss: true);
        var secondSlot = new CompositeTestLease("b", canObserveLoss: true);

        _StubSemaphore(provider, "a", 5, _ => Task.FromResult<IDistributedLease?>(firstSlot));
        _StubSemaphore(provider, "b", 2, _ => Task.FromResult<IDistributedLease?>(secondSlot));

        await using var handle = await provider.AcquireAllAsync(
            [new("a", 5), new("b", 2)],
            new DistributedLockAcquireOptions
            {
                Monitoring = LockMonitoringMode.Monitor,
                TimeUntilExpires = TimeSpan.FromSeconds(30),
            },
            AbortToken
        );

        handle.CanObserveLoss.Should().BeTrue();
        handle.IsLost.Should().BeFalse();

        // Losing either slot loses the whole set — a partially-held set of slots is not a composite.
        secondSlot.MarkLost();

        handle.IsLost.Should().BeTrue();
        handle.LostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task should_reject_slots_that_disagree_on_loss_observability()
    {
        var provider = _CreateProvider(new FakeTimeProvider());

        _StubSemaphore(provider, "a", 5, _ => Task.FromResult<IDistributedLease?>(new CompositeTestLease("a")));
        _StubSemaphore(
            provider,
            "b",
            2,
            _ => Task.FromResult<IDistributedLease?>(new CompositeTestLease("b", canObserveLoss: true))
        );

        var act = async () =>
            await provider.TryAcquireAllAsync([new("a", 5), new("b", 2)], cancellationToken: AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static IDistributedSemaphoreProvider _CreateProvider(FakeTimeProvider timeProvider)
    {
        var provider = Substitute.For<IDistributedSemaphoreProvider>();
        provider.TimeProvider.Returns(timeProvider);
        provider.DefaultAcquireTimeout.Returns(TimeSpan.FromSeconds(30));
        provider.DefaultTimeUntilExpires.Returns(TimeSpan.FromMinutes(20));
        return provider;
    }

    private static IDistributedSemaphore _StubSemaphore(
        IDistributedSemaphoreProvider provider,
        string resource,
        int maxCount,
        Func<CallInfo, Task<IDistributedLease?>> tryAcquire
    )
    {
        var semaphore = Substitute.For<IDistributedSemaphore>();
        semaphore.Resource.Returns(resource);
        semaphore.MaxCount.Returns(maxCount);
        semaphore
            .TryAcquireAsync(Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(tryAcquire);
        provider.CreateSemaphore(resource, maxCount).Returns(semaphore);
        return semaphore;
    }

    private static async Task<IDistributedLease?> _BlockSecondAsync(
        TaskCompletionSource started,
        Task<IDistributedLease?> result
    )
    {
        await Task.Yield();
        started.SetResult();
        return await result.ConfigureAwait(false);
    }
}
