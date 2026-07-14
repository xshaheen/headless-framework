// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;
using Tests.Fakes;

namespace Tests.RegularLocks;

public sealed class CompositeDistributedLockAcquireTests : TestBase
{
    [Fact]
    public async Task should_reject_null_resource_sequence_before_calling_provider()
    {
        var provider = _CreateProvider(new FakeTimeProvider());

        var act = async () => await provider.TryAcquireAllAsync(null!, cancellationToken: AbortToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
        await provider
            .DidNotReceive()
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_validate_complete_resource_set_before_calling_provider()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        IEnumerable<string>[] invalidSets =
        [
            [],
            [null!],
            [""],
            ["  "],
        ];

        foreach (var invalidSet in invalidSets)
        {
            var act = async () => await provider.TryAcquireAllAsync(invalidSet, cancellationToken: AbortToken);
            await act.Should().ThrowAsync<ArgumentException>();
        }

        await provider
            .DidNotReceive()
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_validate_acquire_timeout_before_calling_provider()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        TimeSpan[] invalidTimeouts = [TimeSpan.FromSeconds(-1), TimeSpan.FromMilliseconds(int.MaxValue)];

        foreach (var invalidTimeout in invalidTimeouts)
        {
            var act = async () =>
                await provider.TryAcquireAllAsync(
                    ["A"],
                    new DistributedLockAcquireOptions { AcquireTimeout = invalidTimeout },
                    AbortToken
                );

            await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        }

        await provider
            .DidNotReceive()
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_allow_infinite_acquire_timeout()
    {
        var provider = _CreateProvider(new FakeTimeProvider());

        var act = async () =>
            await provider.TryAcquireAllAsync(
                ["A"],
                new DistributedLockAcquireOptions { AcquireTimeout = Timeout.InfiniteTimeSpan },
                AbortToken
            );

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_enumerate_once_and_acquire_distinct_resources_in_ordinal_order()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = _CreateProvider(timeProvider);
        var calls = new List<string>();
        var enumerationCount = 0;

        IEnumerable<string> Resources()
        {
            enumerationCount++;
            yield return "B";
            yield return "A";
            yield return "B";
        }

        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var resource = call.ArgAt<string>(0);
                calls.Add(resource);
                return Task.FromResult<IDistributedLease?>(new CompositeTestLease(resource));
            });

        var result = await provider.TryAcquireAllAsync(Resources(), cancellationToken: AbortToken);

        result.Should().NotBeNull();
        result!.Resource.Should().Be("A+B");
        enumerationCount.Should().Be(1);
        calls.Should().Equal("A", "B");
    }

    [Fact]
    public async Task should_return_original_child_for_single_canonical_resource()
    {
#pragma warning disable AsyncFixer04 // The lease is intentionally returned by the composite before this test disposes it.
        var provider = _CreateProvider(new FakeTimeProvider());
        await using var child = new CompositeTestLease("A", fencingToken: 42);
        provider
            .TryAcquireAsync("A", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(child));

        var result = await provider.TryAcquireAllAsync(["A", "A"], cancellationToken: AbortToken);

        result.Should().BeSameAs(child);
        result!.LeaseId.Should().Be(child.LeaseId);
        result.FencingToken.Should().Be(42);
        await provider
            .Received(1)
            .TryAcquireAsync("A", Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>());
#pragma warning restore AsyncFixer04
    }

    [Fact]
    public async Task should_share_one_budget_across_child_acquisitions()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = _CreateProvider(timeProvider);
        var observedTimeouts = new List<TimeSpan?>();
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var resource = call.ArgAt<string>(0);
                observedTimeouts.Add(call.ArgAt<DistributedLockAcquireOptions>(1).AcquireTimeout);

                if (string.Equals(resource, "A", StringComparison.Ordinal))
                {
                    timeProvider.Advance(TimeSpan.FromSeconds(3));
                }

                return Task.FromResult<IDistributedLease?>(new CompositeTestLease(resource));
            });

        var result = await provider.TryAcquireAllAsync(
            ["A", "B"],
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
        var attempts = new List<(string Resource, TimeSpan? Timeout)>();
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var resource = call.ArgAt<string>(0);
                attempts.Add((resource, call.ArgAt<DistributedLockAcquireOptions>(1).AcquireTimeout));
                return Task.FromResult<IDistributedLease?>(new CompositeTestLease(resource));
            });

        var result = await provider.TryAcquireAllAsync(
            ["B", "A", "B"],
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.Zero },
            AbortToken
        );

        result.Should().NotBeNull();
        attempts.Select(static attempt => attempt.Resource).Should().Equal("A", "B");
        attempts.Select(static attempt => attempt.Timeout).Should().OnlyContain(timeout => timeout == TimeSpan.Zero);
    }

    [Fact]
    public async Task should_share_provider_default_budget_when_options_timeout_is_null()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = _CreateProvider(timeProvider);
        provider.DefaultAcquireTimeout.Returns(TimeSpan.FromSeconds(10));
        var observedTimeouts = new List<TimeSpan?>();
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var resource = call.ArgAt<string>(0);
                observedTimeouts.Add(call.ArgAt<DistributedLockAcquireOptions>(1).AcquireTimeout);

                if (string.Equals(resource, "A", StringComparison.Ordinal))
                {
                    timeProvider.Advance(TimeSpan.FromSeconds(3));
                }

                return Task.FromResult<IDistributedLease?>(new CompositeTestLease(resource));
            });

        var result = await provider.TryAcquireAllAsync(["A", "B"], cancellationToken: AbortToken);

        result.Should().NotBeNull();
        observedTimeouts.Should().Equal(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task should_release_and_dispose_held_children_when_later_acquire_fails()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var events = new List<string>();
        await using var first = new CompositeTestLease("A", events);
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult<IDistributedLease?>(
                    string.Equals(call.ArgAt<string>(0), "A", StringComparison.Ordinal) ? first : null
                )
            );

        var result = await provider.TryAcquireAllAsync(["A", "B"], cancellationToken: AbortToken);

        result.Should().BeNull();
        events.Should().Equal("release:A", "dispose:A");
    }

    [Fact]
    public async Task should_renew_held_child_at_half_ttl_while_later_acquire_is_pending()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = _CreateProvider(timeProvider);
        await using var first = new CompositeTestLease("A");
        await using var second = new CompositeTestLease("B");
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResult = new TaskCompletionSource<IDistributedLease?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                string.Equals(call.ArgAt<string>(0), "A", StringComparison.Ordinal)
                    ? Task.FromResult<IDistributedLease?>(first)
                    : _BlockSecondAsync(secondStarted, secondResult.Task)
            );

        var acquireTask = provider.TryAcquireAllAsync(
            ["A", "B"],
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

        var result = await acquireTask;

        result.Should().NotBeNull();
        first.RenewalCount.Should().Be(1);
    }

    [Fact]
    public async Task should_preserve_renewal_cadence_across_multiple_later_child_acquisitions()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = _CreateProvider(timeProvider);
        var leases = new Dictionary<string, CompositeTestLease>(StringComparer.Ordinal)
        {
            ["A"] = new("A"),
            ["B"] = new("B"),
            ["C"] = new("C"),
            ["D"] = new("D"),
        };
        var started = new Dictionary<string, TaskCompletionSource>(StringComparer.Ordinal);
        var results = new Dictionary<string, TaskCompletionSource<IDistributedLease?>>(StringComparer.Ordinal);

        foreach (var resource in new[] { "B", "C", "D" })
        {
            started[resource] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            results[resource] = new TaskCompletionSource<IDistributedLease?>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
        }

        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var resource = call.ArgAt<string>(0);

                if (string.Equals(resource, "A", StringComparison.Ordinal))
                {
                    return Task.FromResult<IDistributedLease?>(leases[resource]);
                }

                started[resource].TrySetResult();
                return results[resource].Task;
            });

        var acquireTask = provider.TryAcquireAllAsync(
            ["A", "B", "C", "D"],
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(10),
                AcquireTimeout = Timeout.InfiniteTimeSpan,
            },
            AbortToken
        );

        await started["B"].Task.WaitAsync(AbortToken);
        timeProvider.Advance(TimeSpan.FromSeconds(4));
        results["B"].SetResult(leases["B"]);
        await started["C"].Task.WaitAsync(AbortToken);
        timeProvider.Advance(TimeSpan.FromSeconds(4));
        await CompositeTestScheduler.DrainUntilAsync(() => leases["A"].RenewalCount == 1);
        results["C"].SetResult(leases["C"]);
        await started["D"].Task.WaitAsync(AbortToken);
        timeProvider.Advance(TimeSpan.FromSeconds(4));
        results["D"].SetResult(leases["D"]);

        (await acquireTask).Should().NotBeNull();
        leases["A"].RenewalCount.Should().Be(1);
        leases["B"].RenewalCount.Should().Be(1);
        leases["C"].RenewalCount.Should().Be(0);
    }

    [Fact]
    public async Task should_return_null_after_fake_deadline_and_reverse_rollback()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = _CreateProvider(timeProvider);
        var events = new List<string>();
        await using var first = new CompositeTestLease("A", events);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                string.Equals(call.ArgAt<string>(0), "A", StringComparison.Ordinal)
                    ? Task.FromResult<IDistributedLease?>(first)
                    : _ReturnNullAfterCancellationAsync(secondStarted, call.ArgAt<CancellationToken>(2))
            );

        var acquireTask = provider.TryAcquireAllAsync(
            ["A", "B"],
            new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromSeconds(10) },
            AbortToken
        );

        await secondStarted.Task.WaitAsync(AbortToken);
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        var result = await acquireTask;

        result.Should().BeNull();
        events.Should().Equal("release:A", "dispose:A");
    }

    [Fact]
    public async Task should_cancel_and_drain_blocked_renewal_before_deadline_rollback_returns_null()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = _CreateProvider(timeProvider);
        var events = new List<string>();
        var renewalStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewalCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var first = new CompositeTestLease(
            "A",
            events,
            renewal: (_, cancellationToken) =>
                _BlockRenewalUntilCancellationAsync(renewalStarted, renewalCancelled, cancellationToken)
        );
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                string.Equals(call.ArgAt<string>(0), "A", StringComparison.Ordinal)
                    ? Task.FromResult<IDistributedLease?>(first)
                    : _ReturnNullAfterCancellationAsync(secondStarted, call.ArgAt<CancellationToken>(2))
            );

        var acquireTask = provider.TryAcquireAllAsync(
            ["A", "B"],
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(10),
                AcquireTimeout = TimeSpan.FromSeconds(10),
            },
            AbortToken
        );

        await secondStarted.Task.WaitAsync(AbortToken);
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        await renewalStarted.Task.WaitAsync(AbortToken);
        timeProvider.Advance(TimeSpan.FromSeconds(5));

        var result = await acquireTask;

        result.Should().BeNull();
        renewalCancelled.Task.IsCompleted.Should().BeTrue();
        first.RenewalCount.Should().Be(1);
        events.Should().Equal("release:A", "dispose:A");
    }

    [Fact]
    public async Task should_skip_formation_renewal_for_infinite_ttl()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = _CreateProvider(timeProvider);
        await using var first = new CompositeTestLease("A");
        await using var second = new CompositeTestLease("B");
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResult = new TaskCompletionSource<IDistributedLease?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                string.Equals(call.ArgAt<string>(0), "A", StringComparison.Ordinal)
                    ? Task.FromResult<IDistributedLease?>(first)
                    : _BlockSecondAsync(secondStarted, secondResult.Task)
            );

        var acquireTask = provider.TryAcquireAllAsync(
            ["A", "B"],
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = Timeout.InfiniteTimeSpan,
                AcquireTimeout = Timeout.InfiniteTimeSpan,
            },
            AbortToken
        );

        await secondStarted.Task.WaitAsync(AbortToken);
        timeProvider.Advance(TimeSpan.FromDays(1));
        await Task.Yield();
        first.RenewalCount.Should().Be(0);
        secondResult.SetResult(second);

        (await acquireTask).Should().NotBeNull();
        first.RenewalCount.Should().Be(0);
    }

    [Fact]
    public async Task should_cap_formation_renewal_cadence_at_one_minute()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = _CreateProvider(timeProvider);
        await using var first = new CompositeTestLease("A");
        await using var second = new CompositeTestLease("B");
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResult = new TaskCompletionSource<IDistributedLease?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                string.Equals(call.ArgAt<string>(0), "A", StringComparison.Ordinal)
                    ? Task.FromResult<IDistributedLease?>(first)
                    : _BlockSecondAsync(secondStarted, secondResult.Task)
            );

        var acquireTask = provider.TryAcquireAllAsync(
            ["A", "B"],
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromMinutes(10),
                AcquireTimeout = Timeout.InfiniteTimeSpan,
            },
            AbortToken
        );

        await secondStarted.Task.WaitAsync(AbortToken);
        timeProvider.Advance(TimeSpan.FromSeconds(59));
        await Task.Yield();
        first.RenewalCount.Should().Be(0);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await CompositeTestScheduler.DrainUntilAsync(() => first.RenewalCount == 1);
        secondResult.SetResult(second);

        (await acquireTask).Should().NotBeNull();
    }

    [Fact]
    public async Task should_capture_late_child_and_roll_back_in_reverse_when_held_child_is_lost()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var events = new List<string>();
        await using var first = new CompositeTestLease("A", events, canObserveLoss: true);
        await using var second = new CompositeTestLease("B", events);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (string.Equals(call.ArgAt<string>(0), "A", StringComparison.Ordinal))
                {
                    return Task.FromResult<IDistributedLease?>(first);
                }

                var token = call.ArgAt<CancellationToken>(2);
                secondStarted.SetResult();
                return _ReturnLateAfterCancellationAsync(second, token);
            });

        var acquireTask = provider.TryAcquireAllAsync(
            ["A", "B"],
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(10),
                AcquireTimeout = Timeout.InfiniteTimeSpan,
                Monitoring = LockMonitoringMode.AutoExtend,
            },
            AbortToken
        );

        await secondStarted.Task.WaitAsync(AbortToken);
        first.MarkLost();

        var act = async () => await acquireTask;

        await act.Should().ThrowAsync<LockHandleLostException>().Where(exception => exception.Resource == "A");
        events.Should().Equal("release:B", "dispose:B", "release:A", "dispose:A");
    }

    [Fact]
    public async Task should_prefer_caller_cancellation_and_roll_back_a_late_child()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var events = new List<string>();
        await using var first = new CompositeTestLease("A", events);
        await using var second = new CompositeTestLease("B", events);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var callerSource = new CancellationTokenSource();
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (string.Equals(call.ArgAt<string>(0), "A", StringComparison.Ordinal))
                {
                    return Task.FromResult<IDistributedLease?>(first);
                }

                secondStarted.SetResult();
                return _ReturnLateAfterCancellationAsync(second, call.ArgAt<CancellationToken>(2));
            });

        var acquireTask = provider.TryAcquireAllAsync(
            ["A", "B"],
            new DistributedLockAcquireOptions { AcquireTimeout = Timeout.InfiniteTimeSpan },
            callerSource.Token
        );

        await secondStarted.Task.WaitAsync(AbortToken);
        await callerSource.CancelAsync();
        var act = async () => await acquireTask;

        await act.Should().ThrowAsync<OperationCanceledException>();
        events.Should().Equal("release:B", "dispose:B", "release:A", "dispose:A");
    }

    [Fact]
    public async Task should_flatten_later_child_fault_after_caller_cancellation()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var events = new List<string>();
        await using var first = new CompositeTestLease("A", events);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerError = new InvalidOperationException("acquire failed while draining");
        using var callerSource = new CancellationTokenSource();
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                string.Equals(call.ArgAt<string>(0), "A", StringComparison.Ordinal)
                    ? Task.FromResult<IDistributedLease?>(first)
                    : _ThrowAfterCancellationAsync(secondStarted, providerError, call.ArgAt<CancellationToken>(2))
            );

        var acquireTask = provider.TryAcquireAllAsync(
            ["A", "B"],
            new DistributedLockAcquireOptions { AcquireTimeout = Timeout.InfiniteTimeSpan },
            callerSource.Token
        );

        await secondStarted.Task.WaitAsync(AbortToken);
        await callerSource.CancelAsync();
        var act = async () => await acquireTask;

        var exception = (await act.Should().ThrowAsync<AggregateException>()).Which;
        exception.InnerExceptions.Should().HaveCount(2);
        exception
            .InnerExceptions[0]
            .Should()
            .BeOfType<OperationCanceledException>()
            .Which.CancellationToken.Should()
            .Be(callerSource.Token);
        exception.InnerExceptions[1].Should().BeSameAs(providerError);
        events.Should().Equal("release:A", "dispose:A");
    }

    [Fact]
    public async Task should_abort_and_report_lost_handle_when_mid_flight_renewal_returns_false()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = _CreateProvider(timeProvider);
        var events = new List<string>();
        await using var first = new CompositeTestLease("A", events, renewResult: false);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (string.Equals(call.ArgAt<string>(0), "A", StringComparison.Ordinal))
                {
                    return Task.FromResult<IDistributedLease?>(first);
                }

                return _ReturnNullAfterCancellationAsync(secondStarted, call.ArgAt<CancellationToken>(2));
            });

        var acquireTask = provider.TryAcquireAllAsync(
            ["A", "B"],
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

        await act.Should().ThrowAsync<LockHandleLostException>().Where(exception => exception.Resource == "A");
        first.RenewalCount.Should().Be(1);
        events.Should().Equal("release:A", "dispose:A");
    }

    [Fact]
    public async Task should_renew_all_held_children_concurrently_and_report_failed_child_before_reverse_rollback()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = _CreateProvider(timeProvider);
        var events = new List<string>();
        var renewalsStarted = 0;
        var allRenewalsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var first = new CompositeTestLease(
            "A",
            events,
            renewal: (_, cancellationToken) => RenewAfterAllStartedAsync(true, cancellationToken)
        );
        await using var second = new CompositeTestLease(
            "B",
            events,
            renewal: (_, cancellationToken) => RenewAfterAllStartedAsync(false, cancellationToken)
        );
        var thirdStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> RenewAfterAllStartedAsync(bool result, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref renewalsStarted) == 2)
            {
                allRenewalsStarted.TrySetResult();
            }

            await allRenewalsStarted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }

        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                return call.ArgAt<string>(0) switch
                {
                    "A" => Task.FromResult<IDistributedLease?>(first),
                    "B" => Task.FromResult<IDistributedLease?>(second),
                    _ => _ReturnNullAfterCancellationAsync(thirdStarted, call.ArgAt<CancellationToken>(2)),
                };
            });

        var acquireTask = provider.TryAcquireAllAsync(
            ["A", "B", "C"],
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromSeconds(10),
                AcquireTimeout = Timeout.InfiniteTimeSpan,
            },
            AbortToken
        );

        await thirdStarted.Task.WaitAsync(AbortToken);
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        await allRenewalsStarted.Task.WaitAsync(AbortToken);
        var act = async () => await acquireTask;

        await act.Should().ThrowAsync<LockHandleLostException>().Where(exception => exception.Resource == "B");
        first.RenewalCount.Should().Be(1);
        second.RenewalCount.Should().Be(1);
        events.Should().Equal("release:B", "dispose:B", "release:A", "dispose:A");
    }

    [Fact]
    public async Task should_preserve_mid_flight_renewal_fault_after_rollback()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = _CreateProvider(timeProvider);
        var events = new List<string>();
        var renewalError = new InvalidOperationException("renew failed");
        await using var first = new CompositeTestLease("A", events, renewalException: renewalError);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                string.Equals(call.ArgAt<string>(0), "A", StringComparison.Ordinal)
                    ? Task.FromResult<IDistributedLease?>(first)
                    : _ReturnNullAfterCancellationAsync(secondStarted, call.ArgAt<CancellationToken>(2))
            );

        var acquireTask = provider.TryAcquireAllAsync(
            ["A", "B"],
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

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(renewalError);
        events.Should().Equal("release:A", "dispose:A");
    }

    [Fact]
    public async Task should_surface_cleanup_failure_instead_of_returning_null()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var cleanupError = new InvalidOperationException("release failed");
        await using var first = new CompositeTestLease("A", releaseException: cleanupError);
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult<IDistributedLease?>(
                    string.Equals(call.ArgAt<string>(0), "A", StringComparison.Ordinal) ? first : null
                )
            );

        var act = async () => await provider.TryAcquireAllAsync(["A", "B"], cancellationToken: AbortToken);

        // Cleanup failures carry LockCleanupFailedException so that catch (DistributedLockException) -- the catch-all
        // the package documents on its exception hierarchy -- sees them. The storage exception is preserved on
        // Failures and InnerException rather than being thrown raw, which would have escaped that catch.
        var assertion = await act.Should().ThrowAsync<LockCleanupFailedException>();
        assertion.Which.Failures.Should().ContainSingle().Which.Should().BeSameAs(cleanupError);
        assertion.Which.InnerException.Should().BeSameAs(cleanupError);
        assertion.Which.Should().BeAssignableTo<DistributedLockException>();
    }

    [Fact]
    public async Task should_aggregate_primary_acquire_fault_before_cleanup_fault()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var primary = new InvalidOperationException("acquire failed");
        var cleanup = new IOException("release failed");
        await using var first = new CompositeTestLease("A", releaseException: cleanup);
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                string.Equals(call.ArgAt<string>(0), "A", StringComparison.Ordinal)
                    ? Task.FromResult<IDistributedLease?>(first)
                    : Task.FromException<IDistributedLease?>(primary)
            );

        var act = async () => await provider.TryAcquireAllAsync(["A", "B"], cancellationToken: AbortToken);

        var exception = (await act.Should().ThrowAsync<AggregateException>()).Which;
        exception.InnerExceptions.Should().Equal(primary, cleanup);
    }

    [Fact]
    public async Task should_roll_back_all_children_when_mixed_observability_prevents_composite_construction()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var events = new List<string>();
        await using var first = new CompositeTestLease("A", events);
        await using var second = new CompositeTestLease("B", events, canObserveLoss: true);
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult<IDistributedLease?>(
                    string.Equals(call.ArgAt<string>(0), "A", StringComparison.Ordinal) ? first : second
                )
            );

        var act = async () => await provider.TryAcquireAllAsync(["A", "B"], cancellationToken: AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        events.Should().Equal("release:B", "dispose:B", "release:A", "dispose:A");
    }

    [Fact]
    public async Task should_explicitly_release_and_dispose_failed_formation_when_release_on_dispose_is_false()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var events = new List<string>();
        await using var first = new CompositeTestLease("A", events);
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult<IDistributedLease?>(
                    string.Equals(call.ArgAt<string>(0), "A", StringComparison.Ordinal) ? first : null
                )
            );

        var result = await provider.TryAcquireAllAsync(
            ["A", "B"],
            new DistributedLockAcquireOptions { ReleaseOnDispose = false },
            AbortToken
        );

        result.Should().BeNull();
        events.Should().Equal("release:A", "dispose:A");
    }

    [Fact]
    public async Task should_not_return_composite_when_final_child_is_already_lost()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var events = new List<string>();
        await using var first = new CompositeTestLease("A", events, canObserveLoss: true);
        await using var second = new CompositeTestLease("B", events, canObserveLoss: true);
        second.MarkLost();
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult<IDistributedLease?>(
                    string.Equals(call.ArgAt<string>(0), "A", StringComparison.Ordinal) ? first : second
                )
            );

        var act = async () => await provider.TryAcquireAllAsync(["A", "B"], cancellationToken: AbortToken);

        await act.Should().ThrowAsync<LockHandleLostException>().Where(exception => exception.Resource == "B");
        events.Should().Equal("release:B", "dispose:B", "release:A", "dispose:A");
    }

    [Fact]
    public async Task should_reject_loss_observed_while_linking_composite_tokens()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var events = new List<string>();
        await using var first = new CompositeTestLease("A", events, canObserveLoss: true);
        await using var second = new CompositeTestLease("B", events, canObserveLoss: true, markLostOnTokenRead: 3);
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Task.FromResult<IDistributedLease?>(
                    string.Equals(call.ArgAt<string>(0), "A", StringComparison.Ordinal) ? first : second
                )
            );

        var act = async () => await provider.TryAcquireAllAsync(["A", "B"], cancellationToken: AbortToken);

        await act.Should().ThrowAsync<LockHandleLostException>().Where(exception => exception.Resource == "B");
        events.Should().Equal("release:B", "dispose:B", "release:A", "dispose:A");
    }

    [Fact]
    public async Task should_preserve_first_child_fault_after_caller_cancellation_as_secondary_error()
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        var providerCallStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerResult = new TaskCompletionSource<IDistributedLease?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var providerError = new InvalidOperationException("acquire failed");
        using var callerSource = new CancellationTokenSource();
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                providerCallStarted.TrySetResult();
                return providerResult.Task;
            });

        var acquireTask = provider.TryAcquireAllAsync(["A"], cancellationToken: callerSource.Token);
        await providerCallStarted.Task.WaitAsync(AbortToken);
        await callerSource.CancelAsync();
        providerResult.TrySetException(providerError);
        var act = async () => await acquireTask;

        var exception = (await act.Should().ThrowAsync<AggregateException>()).Which;
        exception.InnerExceptions.Should().HaveCount(2);
        exception
            .InnerExceptions[0]
            .Should()
            .BeOfType<OperationCanceledException>()
            .Which.CancellationToken.Should()
            .Be(callerSource.Token);
        exception.InnerExceptions[1].Should().BeSameAs(providerError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task acquire_all_should_report_canonical_resource_on_failure(bool tryOnce)
    {
        var provider = _CreateProvider(new FakeTimeProvider());
        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IDistributedLease?>(null));
        var options = new DistributedLockAcquireOptions
        {
            AcquireTimeout = tryOnce ? TimeSpan.Zero : TimeSpan.FromSeconds(10),
        };

        var act = async () => await provider.AcquireAllAsync(["B", "A", "B"], options, AbortToken);

        var exception = (await act.Should().ThrowAsync<LockAcquisitionTimeoutException>()).Which;
        exception.Resource.Should().Be("A+B");

        if (tryOnce)
        {
            exception.Message.Should().Contain("first attempt");
        }
    }

    [Fact]
    public async Task should_complete_formation_when_last_child_lands_while_a_renewal_is_in_flight()
    {
        var timeProvider = new FakeTimeProvider();
        var provider = _CreateProvider(timeProvider);
        var renewalStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renewalGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // The held child's renewal is held in flight so the pending acquire can land inside the renewal window --
        // the exact interleaving where the composite must NOT mistake a successful child for an interruption.
        await using var first = new CompositeTestLease(
            "A",
            renewal: async (_, token) =>
            {
                renewalStarted.TrySetResult();
                return await renewalGate.Task.WaitAsync(token).ConfigureAwait(false);
            }
        );

        await using var second = new CompositeTestLease("B");
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResult = new TaskCompletionSource<IDistributedLease?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        Task<IDistributedLease?>? pendingSecond = null;

        provider
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockAcquireOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                string.Equals(call.ArgAt<string>(0), "A", StringComparison.Ordinal)
                    ? Task.FromResult<IDistributedLease?>(first)
                    : pendingSecond = _BlockSecondAsync(secondStarted, secondResult.Task)
            );

        var acquireTask = provider.TryAcquireAllAsync(
            ["A", "B"],
            new DistributedLockAcquireOptions
            {
                TimeUntilExpires = TimeSpan.FromMinutes(10),
                AcquireTimeout = Timeout.InfiniteTimeSpan,
            },
            AbortToken
        );

        await secondStarted.Task.WaitAsync(AbortToken);

        // Fire the renewal cadence, then let B be granted while A's renewal is still outstanding. Awaiting the child
        // and draining the scheduler before releasing the renewal pins the interleaving deterministically: the
        // coordinator observes a COMPLETED child while a renewal is in flight, which it must not mistake for an
        // interrupted wait.
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        await renewalStarted.Task.WaitAsync(AbortToken);
        secondResult.SetResult(second);
        await pendingSecond!.WaitAsync(AbortToken);
        await CompositeTestScheduler.DrainUntilAsync(() => first.RenewalCount == 1);
        renewalGate.TrySetResult(true);

        var handle = await acquireTask.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        handle.Should().NotBeNull();
        handle!.Resource.Should().Be("A+B");
    }

    private static IDistributedLock _CreateProvider(FakeTimeProvider timeProvider)
    {
        var provider = Substitute.For<IDistributedLock>();
        provider.TimeProvider.Returns(timeProvider);
        provider.DefaultAcquireTimeout.Returns(TimeSpan.FromSeconds(30));
        provider.DefaultTimeUntilExpires.Returns(TimeSpan.FromMinutes(20));
        return provider;
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

    private static async Task<IDistributedLease?> _ReturnLateAfterCancellationAsync(
        IDistributedLease lateChild,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }

        return lateChild;
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

    private static async Task<IDistributedLease?> _ThrowAfterCancellationAsync(
        TaskCompletionSource started,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        started.TrySetResult();

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }

        throw exception;
    }

    private static async Task<bool> _BlockRenewalUntilCancellationAsync(
        TaskCompletionSource started,
        TaskCompletionSource cancelled,
        CancellationToken cancellationToken
    )
    {
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            cancellationObserved
        );
        started.TrySetResult();
        await cancellationObserved.Task.ConfigureAwait(false);
        cancelled.TrySetResult();
        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }
}
