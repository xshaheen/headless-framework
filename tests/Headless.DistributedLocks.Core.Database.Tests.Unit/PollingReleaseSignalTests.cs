// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class PollingReleaseSignalTests : TestBase
{
    [Fact]
    public async Task should_keep_shared_entry_until_every_waiter_has_departed()
    {
        var timeProvider = new FakeTimeProvider();
        var signal = new PollingReleaseSignal(timeProvider);
        var timeout = signal.WaitAsync("resource", TimeSpan.FromSeconds(1), AbortToken).AsTask();
        var live = signal.WaitAsync("resource", TimeSpan.FromMinutes(10), AbortToken).AsTask();

        signal.ActiveResourceCount.Should().Be(1);

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await timeout;

        signal.ActiveResourceCount.Should().Be(1);
        live.IsCompleted.Should().BeFalse();

        await signal.PublishAsync("resource", AbortToken);
        await live;

        signal.ActiveResourceCount.Should().Be(0);
    }

    [Fact]
    public async Task should_keep_shared_entry_when_one_waiter_is_cancelled()
    {
        var timeProvider = new FakeTimeProvider();
        var signal = new PollingReleaseSignal(timeProvider);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        var cancelled = signal.WaitAsync("resource", TimeSpan.FromMinutes(10), cancellation.Token).AsTask();
        var live = signal.WaitAsync("resource", TimeSpan.FromMinutes(10), AbortToken).AsTask();

        await cancellation.CancelAsync();

        await FluentActions.Awaiting(() => cancelled).Should().ThrowAsync<OperationCanceledException>();
        signal.ActiveResourceCount.Should().Be(1);
        live.IsCompleted.Should().BeFalse();

        await signal.PublishAsync("resource", AbortToken);
        await live;

        signal.ActiveResourceCount.Should().Be(0);
    }

    [Fact]
    public async Task should_cancel_and_drain_fallback_timer_when_publish_wins()
    {
        var timeProvider = new TrackingTimeProvider();
        var signal = new PollingReleaseSignal(timeProvider);
        var wait = signal.WaitAsync("resource", TimeSpan.FromMinutes(10), AbortToken).AsTask();

        timeProvider.ActiveTimerCount.Should().Be(1);

        await signal.PublishAsync("resource", AbortToken);
        await wait;

        timeProvider.ActiveTimerCount.Should().Be(0);
    }

    [Fact]
    public async Task should_not_attach_later_waiter_to_entry_retired_by_publish_departure_race()
    {
        const int attempts = 100;
        var fallback = TimeSpan.FromSeconds(1);

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var timeProvider = new FakeTimeProvider();
            var signal = new PollingReleaseSignal(timeProvider);
            var resource = $"resource-{attempt}";
            var departing = signal.WaitAsync(resource, fallback, AbortToken).AsTask();
            using var start = new Barrier(3);

            var timeout = Task.Run(() =>
            {
                start.SignalAndWait();
                timeProvider.Advance(fallback);
            });
            var publish = Task.Run(async () =>
            {
                start.SignalAndWait();
                await signal.PublishAsync(resource, AbortToken);
            });

            start.SignalAndWait();
            await Task.WhenAll(timeout, publish);
            await departing;

            signal.ActiveResourceCount.Should().Be(0);

            var later = signal.WaitAsync(resource, fallback, AbortToken).AsTask();

            later.IsCompleted.Should().BeFalse();

            timeProvider.Advance(fallback);
            await later;

            signal.ActiveResourceCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task should_release_final_resource_key_after_fallback()
    {
        var timeProvider = new FakeTimeProvider();
        var signal = new PollingReleaseSignal(timeProvider);
        var resource = await _WaitOnUniqueResourceAsync(signal, timeProvider);

        signal.ActiveResourceCount.Should().Be(0);

        _CollectGarbage();

        resource.TryGetTarget(out _).Should().BeFalse();
        GC.KeepAlive(signal);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference<string>> _WaitOnUniqueResourceAsync(
        PollingReleaseSignal signal,
        FakeTimeProvider timeProvider
    )
    {
        var fallback = TimeSpan.FromSeconds(1);
        var resource = Guid.NewGuid().ToString("N");
        var weakReference = new WeakReference<string>(resource);
        var wait = signal.WaitAsync(resource, fallback, AbortToken).AsTask();

        timeProvider.Advance(fallback);
        await wait;

        return weakReference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void _CollectGarbage()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private sealed class TrackingTimeProvider : TimeProvider
    {
        private readonly FakeTimeProvider _inner = new();
        private int _activeTimerCount;

        public int ActiveTimerCount => Volatile.Read(ref _activeTimerCount);

        public override TimeZoneInfo LocalTimeZone => _inner.LocalTimeZone;

        public override long TimestampFrequency => _inner.TimestampFrequency;

        public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow();

        public override long GetTimestamp() => _inner.GetTimestamp();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = _inner.CreateTimer(callback, state, dueTime, period);
            Interlocked.Increment(ref _activeTimerCount);
            return new TrackingTimer(timer, () => Interlocked.Decrement(ref _activeTimerCount));
        }
    }

    private sealed class TrackingTimer(ITimer inner, Action release) : ITimer
    {
        private int _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period) => inner.Change(dueTime, period);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                inner.Dispose();
            }
            finally
            {
                release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await inner.DisposeAsync();
            }
            finally
            {
                release();
            }
        }
    }
}
