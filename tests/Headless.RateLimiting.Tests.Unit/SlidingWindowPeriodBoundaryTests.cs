// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.RateLimiting;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Tests.RateLimiting;

public sealed class SlidingWindowPeriodBoundaryTests : TestBase
{
    [Fact]
    public async Task should_spin_until_period_key_rotates_after_early_timer_wake()
    {
        // given
        var timeProvider = new EarlyWakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, 99, TimeSpan.Zero));
        var storage = new NonExpiringDistributedRateLimiterStorage();
        var sut = new SlidingWindowDistributedRateLimiter(
            storage,
            new SlidingWindowRateLimiterOptions
            {
                MaxHitsPerPeriod = 1,
                RateLimitingPeriod = TimeSpan.FromMilliseconds(100),
            },
            timeProvider,
            LoggerFactory.CreateLogger<SlidingWindowDistributedRateLimiter>()
        );
        var resource = Faker.Random.AlphaNumeric(10);

        await sut.TryAcquireAsync(resource, TimeSpan.FromSeconds(1), AbortToken);

        // when
        var result = await sut.TryAcquireAsync(resource, TimeSpan.FromSeconds(1), AbortToken);

        // then
        result.Should().NotBeNull();
        storage.IncrementCount.Should().Be(2);
        timeProvider.DelayDurations.Should().Contain(TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task should_retry_with_fresh_period_when_period_rotates_between_count_and_increment()
    {
        // given
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, 99, TimeSpan.Zero));
        var storage = new PeriodRotatingDistributedRateLimiterStorage(timeProvider);
        var sut = new SlidingWindowDistributedRateLimiter(
            storage,
            new SlidingWindowRateLimiterOptions
            {
                MaxHitsPerPeriod = 1,
                RateLimitingPeriod = TimeSpan.FromMilliseconds(100),
            },
            timeProvider,
            LoggerFactory.CreateLogger<SlidingWindowDistributedRateLimiter>()
        );
        var resource = Faker.Random.AlphaNumeric(10);

        // when
        var result = await sut.TryAcquireAsync(resource, TimeSpan.FromSeconds(1), AbortToken);

        // then
        result.Should().NotBeNull();
        storage.IncrementTtls.Should().ContainSingle(ttl => ttl > TimeSpan.Zero);
    }

    [Fact]
    public async Task should_log_warning_when_period_does_not_rotate_after_spin_cap()
    {
        // given
        var timeProvider = new FrozenTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, 99, TimeSpan.Zero));
        var storage = new NonExpiringDistributedRateLimiterStorage();
        var logger = Substitute.For<ILogger<SlidingWindowDistributedRateLimiter>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var captured = new List<(LogLevel Level, int Id)>();
        logger
            .When(l =>
                l.Log(
                    Arg.Any<LogLevel>(),
                    Arg.Any<EventId>(),
                    Arg.Any<object>(),
                    Arg.Any<Exception?>(),
                    Arg.Any<Func<object, Exception?, string>>()
                )
            )
            .Do(call => captured.Add((call.Arg<LogLevel>(), call.Arg<EventId>().Id)));
        var sut = new SlidingWindowDistributedRateLimiter(
            storage,
            new SlidingWindowRateLimiterOptions
            {
                MaxHitsPerPeriod = 1,
                RateLimitingPeriod = TimeSpan.FromMilliseconds(100),
            },
            timeProvider,
            logger
        );
        var resource = Faker.Random.AlphaNumeric(10);

        await sut.TryAcquireAsync(resource, TimeSpan.FromSeconds(1), AbortToken);

        // when
        var result = await sut.TryAcquireAsync(resource, TimeSpan.FromMilliseconds(10), AbortToken);

        // then
        result.Should().BeNull();
        captured.Should().Contain(e => e.Level == LogLevel.Warning && e.Id == 12);
    }

    private sealed class EarlyWakeTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private int _delayCount;
        private DateTimeOffset _utcNow = initialUtcNow;

        public List<TimeSpan> DelayDurations { get; } = [];

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _utcNow.UtcTicks;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            DelayDurations.Add(dueTime);
            _delayCount++;

            if (_delayCount > 1)
            {
                _utcNow += dueTime;
            }

            ThreadPool.QueueUserWorkItem(_ => callback(state));

            return new NoOpTimer();
        }
    }

    private sealed class NoOpTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NonExpiringDistributedRateLimiterStorage : IDistributedRateLimiterStorage
    {
        private readonly ConcurrentDictionary<string, long> _counters = new(StringComparer.Ordinal);

        public int IncrementCount { get; private set; }

        public Task<long> GetHitCountsAsync(string resource, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(_counters.GetValueOrDefault(resource));
        }

        public Task<long> IncrementAsync(string resource, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IncrementCount++;

            return Task.FromResult(_counters.AddOrUpdate(resource, 1, (_, count) => count + 1));
        }

        public ValueTask DisposeAsync()
        {
            _counters.Clear();

            return ValueTask.CompletedTask;
        }
    }

    private sealed class FrozenTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => utcNow.UtcTicks;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ThreadPool.QueueUserWorkItem(_ => callback(state));

            return new NoOpTimer();
        }
    }

    private sealed class PeriodRotatingDistributedRateLimiterStorage(FakeTimeProvider timeProvider)
        : IDistributedRateLimiterStorage
    {
        private bool _periodRotated;

        public List<TimeSpan> IncrementTtls { get; } = [];

        public Task<long> GetHitCountsAsync(string resource, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_periodRotated)
            {
                _periodRotated = true;
                timeProvider.Advance(TimeSpan.FromMilliseconds(2));
            }

            return Task.FromResult(0L);
        }

        public Task<long> IncrementAsync(string resource, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ttl.Should().BePositive();
            IncrementTtls.Add(ttl);

            return Task.FromResult(1L);
        }

        public ValueTask DisposeAsync()
        {
            IncrementTtls.Clear();

            return ValueTask.CompletedTask;
        }
    }
}
