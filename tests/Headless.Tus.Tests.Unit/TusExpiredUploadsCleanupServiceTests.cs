// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Tus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using tusdotnet.Interfaces;

namespace Tests;

/// <summary>A store that can expire uploads, as one type NSubstitute can stand in for.</summary>
public interface IExpiringTusStore : ITusStore, ITusExpirationStore;

public sealed class TusExpiredUploadsCleanupServiceTests : TestBase
{
    private static readonly TimeSpan _Interval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Advances fake time one interval at a time with small real-time yields until the signal
    /// completes. A single Advance can race the service's async loop before it re-arms
    /// PeriodicTimer.WaitForNextTickAsync (same pattern as the coordination heartbeat tests).
    /// </summary>
    private static async Task _AdvanceUntilAsync(FakeTimeProvider timeProvider, Task signal)
    {
        for (var i = 0; i < 50 && !signal.IsCompleted; i++)
        {
            timeProvider.Advance(_Interval);
            await Task.Delay(10, AbortToken);
        }

        await signal.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
    }

    private static TusExpiredUploadsCleanupService _CreateService(
        ITusStore store,
        FakeTimeProvider timeProvider,
        ITusExpiredUploadsCleanupGate? gate = null,
        bool enabled = true
    )
    {
        return new TusExpiredUploadsCleanupService(
            store,
            Options.Create(new TusExpiredUploadsCleanupOptions { Interval = _Interval, Enabled = enabled }),
            timeProvider,
            NullLogger<TusExpiredUploadsCleanupService>.Instance,
            gate
        );
    }

    [Fact]
    public async Task should_skip_the_pass_when_another_node_claimed_it()
    {
        // given
        var store = Substitute.For<IExpiringTusStore>();
        var gate = new _FakeGate(claims: false);

        var timeProvider = new FakeTimeProvider();
        using var service = _CreateService(store, timeProvider, gate);

        // when
        await service.StartAsync(AbortToken);
        await gate.Attempted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await service.StopAsync(AbortToken);

        // then
        await store.DidNotReceive().RemoveExpiredFilesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_run_the_pass_and_hold_the_claim_for_most_of_the_interval_when_claimed()
    {
        // given
        var store = Substitute.For<IExpiringTusStore>();
        var firstPass = new TaskCompletionSource();
        store.RemoveExpiredFilesAsync(Arg.Any<CancellationToken>()).Returns(1).AndDoes(_ => firstPass.TrySetResult());
        var gate = new _FakeGate(claims: true);

        var timeProvider = new FakeTimeProvider();
        using var service = _CreateService(store, timeProvider, gate);

        // when
        await service.StartAsync(AbortToken);
        await firstPass.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await service.StopAsync(AbortToken);

        // then
        gate.HoldFor.Should().Be(_Interval - (_Interval / 10));
    }

    // A hand-written fake: the gate interface is internal, which NSubstitute's proxy assembly cannot see.
    private sealed class _FakeGate(bool claims) : ITusExpiredUploadsCleanupGate
    {
        public TaskCompletionSource Attempted { get; } = new();

        public TimeSpan? HoldFor { get; private set; }

        public Task<bool> TryClaimPassAsync(TimeSpan holdFor, CancellationToken cancellationToken)
        {
            HoldFor = holdFor;
            Attempted.TrySetResult();

            return Task.FromResult(claims);
        }
    }

    [Fact]
    public void should_resolve_without_a_gate_when_no_lock_provider_is_registered()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessTus(tus => tus.UseStore(Substitute.For<IExpiringTusStore>()));

        // when
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetService<ITusExpiredUploadsCleanupGate>().Should().BeNull();
        provider
            .GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .Should()
            .ContainSingle(service => service is TusExpiredUploadsCleanupService);
    }

    [Fact]
    public async Task should_end_without_a_pass_when_disabled()
    {
        // given
        var store = Substitute.For<IExpiringTusStore>();
        var timeProvider = new FakeTimeProvider();
        using var service = _CreateService(store, timeProvider, enabled: false);

        // when - the loop ends at once, so no interval has to pass
        await service.StartAsync(AbortToken);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        await store.DidNotReceive().RemoveExpiredFilesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_end_without_a_pass_when_the_store_cannot_expire_uploads()
    {
        // given - a store with no ITusExpirationStore, and a gate that would record a claim attempt
        var store = Substitute.For<ITusStore>();
        var gate = new _FakeGate(claims: true);
        var timeProvider = new FakeTimeProvider();
        using var service = _CreateService(store, timeProvider, gate);

        // when
        await service.StartAsync(AbortToken);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then - it never competed for the pass it could not run
        gate.HoldFor.Should().BeNull();
    }

    [Fact]
    public async Task should_run_a_cleanup_pass_immediately_at_startup()
    {
        // given
        var store = Substitute.For<IExpiringTusStore>();
        var firstPass = new TaskCompletionSource();
        store.RemoveExpiredFilesAsync(Arg.Any<CancellationToken>()).Returns(1).AndDoes(_ => firstPass.TrySetResult());

        var timeProvider = new FakeTimeProvider();
        using var service = _CreateService(store, timeProvider);

        // when - no time is advanced at all
        await service.StartAsync(AbortToken);
        await firstPass.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await service.StopAsync(AbortToken);

        // then - the startup pass ran without waiting for the first interval
        await store.Received().RemoveExpiredFilesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_remove_expired_uploads_each_interval()
    {
        // given - the startup pass satisfies call #1, so the interval loop is only proven by a
        // SECOND call that requires advancing fake time
        var store = Substitute.For<IExpiringTusStore>();
        var secondPass = new TaskCompletionSource();
        var calls = 0;
        store
            .RemoveExpiredFilesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref calls) >= 2)
                {
                    secondPass.TrySetResult();
                }

                return 3;
            });

        var timeProvider = new FakeTimeProvider();
        using var service = _CreateService(store, timeProvider);

        // when
        await service.StartAsync(AbortToken);
        await _AdvanceUntilAsync(timeProvider, secondPass.Task);
        await service.StopAsync(AbortToken);

        // then - startup pass + at least one interval-driven pass
        calls.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task should_keep_running_after_a_failed_pass()
    {
        // given - the first pass throws, the second succeeds
        var store = Substitute.For<IExpiringTusStore>();
        var secondPass = new TaskCompletionSource();
        var calls = 0;
        store
            .RemoveExpiredFilesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;

                if (calls == 1)
                {
                    throw new InvalidOperationException("storage hiccup");
                }

                secondPass.TrySetResult();

                return 0;
            });

        var timeProvider = new FakeTimeProvider();
        using var service = _CreateService(store, timeProvider);

        // when
        await service.StartAsync(AbortToken);
        await _AdvanceUntilAsync(timeProvider, secondPass.Task);
        await service.StopAsync(AbortToken);

        // then - the loop survived the failure
        calls.Should().BeGreaterThanOrEqualTo(2);
    }
}
