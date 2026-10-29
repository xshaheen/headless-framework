// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class DeadOwnerReconcileIntervalTests : TestBase
{
    private static readonly CoordinationOptions _DefaultCoordination = new();

    [Fact]
    public void should_default_to_half_the_dead_retention_window_without_a_warning()
    {
        var logger = new RecordingLogger();

        var interval = DeadOwnerReconcileInterval.Resolve(configured: null, _DefaultCoordination, logger);

        interval.Should().Be(TimeSpan.FromSeconds(15));
        logger.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    public void should_keep_a_configured_interval_at_or_below_the_ceiling(int seconds)
    {
        var logger = new RecordingLogger();

        var interval = DeadOwnerReconcileInterval.Resolve(TimeSpan.FromSeconds(seconds), _DefaultCoordination, logger);

        interval.Should().Be(TimeSpan.FromSeconds(seconds));
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public void should_clamp_a_configured_interval_above_the_ceiling_and_warn_once()
    {
        var logger = new RecordingLogger();

        var interval = DeadOwnerReconcileInterval.Resolve(TimeSpan.FromMinutes(1), _DefaultCoordination, logger);

        interval.Should().Be(TimeSpan.FromSeconds(15));
        logger.Entries.Should().ContainSingle();
        logger.Entries[0].Level.Should().Be(LogLevel.Warning);
        logger.Entries[0].EventId.Should().Be(14);
        logger.Entries[0].Message.Should().Contain("00:01:00").And.Contain("00:00:15").And.Contain("00:00:30");
    }

    [Fact]
    public void should_keep_a_configured_interval_without_a_warning_when_the_host_has_no_membership_provider()
    {
        // Messaging registers NullNodeMembership as its fallback while the options system still resolves default
        // coordination options, so a host with no coordination provider must not be clamped against that window.
        var logger = new RecordingLogger();

        var interval = DeadOwnerReconcileInterval.Resolve(
            TimeSpan.FromMinutes(1),
            _DefaultCoordination,
            logger,
            new NullNodeMembership()
        );

        interval.Should().Be(TimeSpan.FromMinutes(1));
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public void should_keep_a_configured_interval_when_coordination_is_absent()
    {
        var logger = new RecordingLogger();

        var interval = DeadOwnerReconcileInterval.Resolve(TimeSpan.FromMinutes(1), coordination: null, logger);

        interval.Should().Be(TimeSpan.FromMinutes(1));
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public void should_default_to_half_the_stock_retention_window_when_coordination_is_absent()
    {
        var interval = DeadOwnerReconcileInterval.Resolve(configured: null, coordination: null, new RecordingLogger());

        interval.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void should_keep_a_configured_interval_when_the_retention_window_is_not_positive()
    {
        var logger = new RecordingLogger();

        var interval = DeadOwnerReconcileInterval.Resolve(
            TimeSpan.FromMinutes(1),
            new CoordinationOptions { DeadRetentionWindow = TimeSpan.Zero },
            logger
        );

        interval.Should().Be(TimeSpan.FromMinutes(1));
        logger.Entries.Should().BeEmpty();
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, int EventId, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add((logLevel, eventId.Id, formatter(state, exception)));
        }
    }
}
