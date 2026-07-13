// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;

namespace Tests;

public sealed class CompositeDistributedLeaseTests : TestBase
{
    private static readonly DateTimeOffset _AcquiredAt = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan _Waited = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task should_project_composite_metadata()
    {
        var first = new TestLease("a", "lease-a") { RenewalCount = 4 };
        var second = new TestLease("b", "lease-b") { RenewalCount = 2 };

        await using var sut = _Create([first, second], releaseOnDispose: false);

        sut.Resource.Should().Be("a+b");
        sut.LeaseId.Should().NotBe(first.LeaseId).And.NotBe(second.LeaseId);
        Guid.TryParseExact(sut.LeaseId, "N", out _).Should().BeTrue();
        sut.FencingToken.Should().BeNull();
        sut.DateAcquired.Should().Be(_AcquiredAt);
        sut.TimeWaitedForLock.Should().Be(_Waited);
        sut.RenewalCount.Should().Be(2);
        sut.CanObserveLoss.Should().BeFalse();
        sut.LostToken.Should().Be(CancellationToken.None);
    }

    [Fact]
    public async Task should_link_loss_from_every_child()
    {
        using var firstLost = new CancellationTokenSource();
        using var secondLost = new CancellationTokenSource();
        var first = new TestLease("a", "lease-a", firstLost.Token);
        var second = new TestLease("b", "lease-b", secondLost.Token);
        await using var sut = _Create([first, second], releaseOnDispose: false);

        sut.CanObserveLoss.Should().BeTrue();
        sut.LostToken.IsCancellationRequested.Should().BeFalse();

        await secondLost.CancelAsync();

        sut.LostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void should_reject_mixed_loss_observability()
    {
        using var lost = new CancellationTokenSource();
        var children = new IDistributedLease[]
        {
            new TestLease("a", "lease-a", lost.Token),
            new TestLease("b", "lease-b"),
        };

        var act = () => _Create(children);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task should_renew_all_children_and_return_false_when_any_child_is_lost()
    {
        var first = new TestLease("a", "lease-a");
        var second = new TestLease("b", "lease-b") { Renew = static (_, _) => Task.FromResult(false) };
        await using var sut = _Create([first, second], releaseOnDispose: false);
        var ttl = TimeSpan.FromMinutes(5);

        var result = await sut.RenewAsync(ttl, AbortToken);

        result.Should().BeFalse();
        first.RenewCalls.Should().Be(1);
        second.RenewCalls.Should().Be(1);
        first.LastRenewalTtl.Should().Be(ttl);
        second.LastRenewalToken.Should().Be(AbortToken);
    }

    [Fact]
    public async Task should_aggregate_concurrent_renewal_faults()
    {
        var firstError = new InvalidOperationException("first");
        var secondError = new ApplicationException("second");
        var first = new TestLease("a", "lease-a") { Renew = (_, _) => Task.FromException<bool>(firstError) };
        var second = new TestLease("b", "lease-b") { Renew = (_, _) => Task.FromException<bool>(secondError) };
        await using var sut = _Create([first, second], releaseOnDispose: false);

        var act = async () => await sut.RenewAsync(cancellationToken: AbortToken);

        var assertion = await act.Should().ThrowAsync<AggregateException>();
        assertion.Which.InnerExceptions.Should().ContainInOrder(firstError, secondError);
    }

    [Fact]
    public async Task should_surface_cancellation_without_aggregating_child_cancellations()
    {
        using var cancellation = new CancellationTokenSource();
        var renewalsStarted = 0;
        var allRenewalsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<TimeSpan?, CancellationToken, Task<bool>> renew = async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref renewalsStarted) == 2)
            {
                allRenewalsStarted.TrySetResult();
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return true;
        };
        var first = new TestLease("a", "lease-a") { Renew = renew };
        var second = new TestLease("b", "lease-b") { Renew = renew };
        await using var sut = _Create([first, second], releaseOnDispose: false);

        var renewal = sut.RenewAsync(cancellationToken: cancellation.Token);
        await allRenewalsStarted.Task.WaitAsync(AbortToken);
        await cancellation.CancelAsync();

        var act = async () => await renewal;

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_release_in_reverse_and_retry_after_failure()
    {
        var calls = new List<string>();
        var firstAttempt = true;
        var firstError = new InvalidOperationException("release-a");
        var first = new TestLease("a", "lease-a")
        {
            Release = () =>
            {
                calls.Add("release:a");

                if (firstAttempt)
                {
                    firstAttempt = false;
                    return Task.FromException(firstError);
                }

                return Task.CompletedTask;
            },
        };
        var second = new TestLease("b", "lease-b")
        {
            Release = () =>
            {
                calls.Add("release:b");
                return Task.CompletedTask;
            },
        };
        await using var sut = _Create([first, second], releaseOnDispose: false);

        var firstRelease = async () => await sut.ReleaseAsync();
        var assertion = await firstRelease.Should().ThrowAsync<InvalidOperationException>();
        assertion.Which.Should().BeSameAs(firstError);

        await sut.ReleaseAsync();

        calls.Should().ContainInOrder("release:b", "release:a", "release:b", "release:a");
    }

    [Fact]
    public async Task should_release_then_dispose_every_child_in_reverse_order()
    {
        var calls = new List<string>();
        var first = _CreateOrderedLease("a", calls);
        var second = _CreateOrderedLease("b", calls);
        var sut = _Create([first, second], releaseOnDispose: true);

        await sut.DisposeAsync();

        calls.Should().ContainInOrder("release:b", "release:a", "dispose:b", "dispose:a");
    }

    [Fact]
    public async Task dispose_should_aggregate_release_and_dispose_failures_after_attempting_every_child()
    {
        var releaseError = new InvalidOperationException("release-b");
        var disposeError = new ApplicationException("dispose-a");
        var first = new TestLease("a", "lease-a") { Dispose = () => ValueTask.FromException(disposeError) };
        var second = new TestLease("b", "lease-b") { Release = () => Task.FromException(releaseError) };
        var sut = _Create([first, second], releaseOnDispose: true);

        var act = async () => await sut.DisposeAsync();

        var assertion = await act.Should().ThrowAsync<AggregateException>();
        assertion.Which.InnerExceptions.Should().ContainInOrder(releaseError, disposeError);
        first.ReleaseCalls.Should().Be(1);
        second.ReleaseCalls.Should().Be(1);
        first.DisposeCalls.Should().Be(1);
        second.DisposeCalls.Should().Be(1);
    }

    [Fact]
    public async Task should_serialize_renewal_and_release()
    {
        var renewalStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRenewal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCalls = 0;
        var first = new TestLease("a", "lease-a")
        {
            Renew = async (_, _) =>
            {
                renewalStarted.TrySetResult();
                await allowRenewal.Task.ConfigureAwait(false);
                return true;
            },
            Release = () =>
            {
                Interlocked.Increment(ref releaseCalls);
                return Task.CompletedTask;
            },
        };
        var second = new TestLease("b", "lease-b");
        await using var sut = _Create([first, second], releaseOnDispose: false);

        var renewal = sut.RenewAsync(cancellationToken: AbortToken);
        await renewalStarted.Task.WaitAsync(AbortToken);
        var release = sut.ReleaseAsync();
        await Task.Yield();

        Volatile.Read(ref releaseCalls).Should().Be(0);
        allowRenewal.TrySetResult();
        (await renewal).Should().BeTrue();
        await release;
        releaseCalls.Should().Be(1);
        (await sut.RenewAsync(cancellationToken: AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_share_one_idempotent_dispose_operation_across_callers()
    {
        var disposeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowDispose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new TestLease("a", "lease-a");
        var second = new TestLease("b", "lease-b")
        {
            Dispose = async () =>
            {
                disposeStarted.TrySetResult();
                await allowDispose.Task.ConfigureAwait(false);
            },
        };
        var sut = _Create([first, second], releaseOnDispose: false);

        var firstDispose = sut.DisposeAsync().AsTask();
        await disposeStarted.Task.WaitAsync(AbortToken);
        var secondDispose = sut.DisposeAsync().AsTask();
        allowDispose.TrySetResult();
        await Task.WhenAll(firstDispose, secondDispose);

        first.DisposeCalls.Should().Be(1);
        second.DisposeCalls.Should().Be(1);
    }

    [Fact]
    public async Task should_allow_renew_and_explicit_release_after_no_release_disposal()
    {
        var first = new TestLease("a", "lease-a");
        var second = new TestLease("b", "lease-b");
        var sut = _Create([first, second], releaseOnDispose: false);

        await sut.DisposeAsync();

        (await sut.RenewAsync(cancellationToken: AbortToken)).Should().BeTrue();
        await sut.ReleaseAsync();
        first.ReleaseCalls.Should().Be(1);
        second.ReleaseCalls.Should().Be(1);
        (await sut.RenewAsync(cancellationToken: AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task provider_extensions_should_fan_out_without_touching_composite_lifecycle()
    {
        var provider = Substitute.For<IDistributedLock>();
        var first = new TestLease("a", "lease-a");
        var second = new TestLease("b", "lease-b");
        await using var sut = _Create([first, second], releaseOnDispose: false);
        var ttl = TimeSpan.FromMinutes(7);
        provider.RenewAsync("a", "lease-a", ttl, AbortToken).Returns(true);
        provider.RenewAsync("b", "lease-b", ttl, AbortToken).Returns(true);
        provider.ReleaseAsync(Arg.Any<string>(), Arg.Any<string>(), AbortToken).Returns(Task.CompletedTask);

        var renewed = await provider.RenewAsync(sut, ttl, AbortToken);
        await provider.ReleaseAsync(sut, AbortToken);

        renewed.Should().BeTrue();
        first.RenewCalls.Should().Be(0);
        second.RenewCalls.Should().Be(0);
        first.ReleaseCalls.Should().Be(0);
        second.ReleaseCalls.Should().Be(0);
        await provider.Received(1).RenewAsync("a", "lease-a", ttl, AbortToken);
        await provider.Received(1).RenewAsync("b", "lease-b", ttl, AbortToken);
        await provider.Received(1).ReleaseAsync("b", "lease-b", AbortToken);
        await provider.Received(1).ReleaseAsync("a", "lease-a", AbortToken);
        await provider.DidNotReceive().ReleaseAsync(sut.Resource, sut.LeaseId, Arg.Any<CancellationToken>());

        (await sut.RenewAsync(cancellationToken: AbortToken)).Should().BeTrue();
    }

    private static CompositeDistributedLease _Create(
        IReadOnlyList<IDistributedLease> children,
        bool releaseOnDispose = true
    )
    {
        return new CompositeDistributedLease(children, _AcquiredAt, _Waited, releaseOnDispose);
    }

    private static TestLease _CreateOrderedLease(string resource, List<string> calls)
    {
        return new TestLease(resource, $"lease-{resource}")
        {
            Release = () =>
            {
                calls.Add($"release:{resource}");
                return Task.CompletedTask;
            },
            Dispose = () =>
            {
                calls.Add($"dispose:{resource}");
                return ValueTask.CompletedTask;
            },
        };
    }

    private sealed class TestLease(string resource, string leaseId, CancellationToken lostToken = default)
        : IDistributedLease
    {
        public Func<TimeSpan?, CancellationToken, Task<bool>> Renew { get; init; } =
            static (_, _) => Task.FromResult(true);

        public Func<Task> Release { get; init; } = static () => Task.CompletedTask;

        public Func<ValueTask> Dispose { get; init; } = static () => ValueTask.CompletedTask;

        public string LeaseId { get; } = leaseId;

        public long? FencingToken { get; init; }

        public string Resource { get; } = resource;

        public int RenewalCount { get; set; }

        public DateTimeOffset DateAcquired { get; init; } = _AcquiredAt;

        public TimeSpan TimeWaitedForLock { get; init; }

        public CancellationToken LostToken { get; } = lostToken;

        public bool CanObserveLoss => LostToken.CanBeCanceled;

        public int RenewCalls { get; private set; }

        public int ReleaseCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public TimeSpan? LastRenewalTtl { get; private set; }

        public CancellationToken LastRenewalToken { get; private set; }

        public async Task<bool> RenewAsync(
            TimeSpan? timeUntilExpires = null,
            CancellationToken cancellationToken = default
        )
        {
            RenewCalls++;
            LastRenewalTtl = timeUntilExpires;
            LastRenewalToken = cancellationToken;

            return await Renew(timeUntilExpires, cancellationToken).ConfigureAwait(false);
        }

        public async Task ReleaseAsync()
        {
            ReleaseCalls++;
            await Release().ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            DisposeCalls++;
            await Dispose().ConfigureAwait(false);
        }
    }
}
