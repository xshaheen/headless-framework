// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Cronos;
using Headless.Messaging.Scheduling;
using Headless.Testing.Tests;

namespace Tests.Scheduling;

public sealed class CronScheduleCacheTests : TestBase
{
    private readonly CronScheduleCache _sut = new();

    // -- next occurrence --

    [Fact]
    public void should_return_next_occurrence_for_valid_cron()
    {
        // given  — every 5 seconds
        var from = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

        // when
        var next = _sut.GetNextOccurrence("*/5 * * * * *", null, from);

        // then
        next.Should().NotBeNull();
        next!.Value.Should().Be(new DateTimeOffset(2025, 6, 1, 12, 0, 5, TimeSpan.Zero));
    }

    [Fact]
    public void should_return_null_offset_for_utc()
    {
        var from = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var next = _sut.GetNextOccurrence("0 0 * * * *", null, from);

        next.Should().NotBeNull();
        next!.Value.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void should_use_utc_when_timezone_is_null()
    {
        var from = new DateTimeOffset(2025, 3, 15, 10, 0, 0, TimeSpan.Zero);

        var next = _sut.GetNextOccurrence("0 0 0 * * *", null, from);

        // next midnight UTC
        next.Should().Be(new DateTimeOffset(2025, 3, 16, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void should_use_utc_when_timezone_is_empty()
    {
        var from = new DateTimeOffset(2025, 3, 15, 10, 0, 0, TimeSpan.Zero);

        var next = _sut.GetNextOccurrence("0 0 0 * * *", "", from);

        next.Should().Be(new DateTimeOffset(2025, 3, 16, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void should_use_utc_when_timezone_is_whitespace()
    {
        var from = new DateTimeOffset(2025, 3, 15, 10, 0, 0, TimeSpan.Zero);

        var next = _sut.GetNextOccurrence("0 0 0 * * *", "   ", from);

        next.Should().Be(new DateTimeOffset(2025, 3, 16, 0, 0, 0, TimeSpan.Zero));
    }

    // -- timezone handling --

    [Fact]
    public void should_respect_timezone_when_calculating_next_occurrence()
    {
        // given — midnight in US Eastern (UTC-5 in winter)
        var from = new DateTimeOffset(2025, 1, 15, 4, 0, 0, TimeSpan.Zero); // 11pm ET Jan 14

        // when — cron = every day at midnight (seconds=0, min=0, hour=0)
        var next = _sut.GetNextOccurrence("0 0 0 * * *", "America/New_York", from);

        // then — next midnight ET = 2025-01-15 05:00 UTC (EST = UTC-5)
        next.Should().NotBeNull();
        next!.Value.Should().Be(new DateTimeOffset(2025, 1, 15, 5, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void should_throw_for_invalid_timezone()
    {
        var from = DateTimeOffset.UtcNow;

        var act = () => _sut.GetNextOccurrence("0 0 * * * *", "Invalid/Timezone", from);

        act.Should().Throw<TimeZoneNotFoundException>();
    }

    // -- cron parsing --

    [Fact]
    public void should_throw_for_invalid_cron_expression()
    {
        var from = DateTimeOffset.UtcNow;

        var act = () => _sut.GetNextOccurrence("not a cron", null, from);

        act.Should().Throw<CronFormatException>();
    }

    [Fact]
    public void should_throw_for_null_cron_expression()
    {
        var from = DateTimeOffset.UtcNow;

        var act = () => _sut.GetNextOccurrence(null!, null, from);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_parse_six_field_cron_with_seconds()
    {
        // given — at second 30 of every minute
        var from = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

        // when
        var next = _sut.GetNextOccurrence("30 * * * * *", null, from);

        // then
        next.Should().Be(new DateTimeOffset(2025, 6, 1, 12, 0, 30, TimeSpan.Zero));
    }

    // -- caching --

    [Fact]
    public void should_cache_parsed_expression()
    {
        var from = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var next1 = _sut.GetNextOccurrence("*/5 * * * * *", null, from);
        var next2 = _sut.GetNextOccurrence("*/5 * * * * *", null, from);

        next1.Should().Be(next2);
    }

    [Fact]
    public void should_normalize_whitespace_and_cache_as_same_entry()
    {
        var from = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var next1 = _sut.GetNextOccurrence("*/5 * * * * *", null, from);
        var next2 = _sut.GetNextOccurrence("*/5    *   *   *   *   *", null, from);

        next1.Should().Be(next2);
    }

    // -- thread safety --

    [Fact]
    public void should_be_thread_safe()
    {
        var from = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var results = new ConcurrentBag<DateTimeOffset?>();
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(
            0,
            1000,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            i =>
            {
                try
                {
                    var next = _sut.GetNextOccurrence("*/5 * * * * *", null, from);
                    results.Add(next);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        );

        exceptions.Should().BeEmpty("CronScheduleCache should be thread-safe");
        results.Should().HaveCount(1000);
        results.Should().AllSatisfy(r => r.Should().NotBeNull());
    }

    [Fact]
    public void should_be_thread_safe_with_different_expressions()
    {
        var from = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(
            0,
            100,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            i =>
            {
                try
                {
                    _sut.GetNextOccurrence($"*/{i % 59 + 1} * * * * *", null, from);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }
        );

        exceptions.Should().BeEmpty("concurrent access with distinct expressions should be safe");
    }
}
