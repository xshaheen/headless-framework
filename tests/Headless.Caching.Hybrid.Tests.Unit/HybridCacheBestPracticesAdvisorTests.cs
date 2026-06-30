// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// Verifies that the startup best-practices advisor emits the expected <see cref="LogLevel.Warning"/>
/// log events for questionable-but-valid <see cref="HybridCacheOptions"/> configurations, and that it
/// stays silent on a well-formed configuration.
/// </summary>
public sealed class HybridCacheBestPracticesAdvisorTests : TestBase
{
    // ────────────────────────────────────────────────────────────
    // Check 1 — AutoRecoveryDelay > 5 minutes
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task should_warn_when_auto_recovery_enabled_and_delay_exceeds_threshold()
    {
        var options = new HybridCacheOptions
        {
            EnableAutoRecovery = true,
            AutoRecoveryDelay = TimeSpan.FromMinutes(6), // > 5-minute threshold
        };

        var logger = new CapturingLogger();
        var advisor = new HybridCacheBestPracticesAdvisor(options, logger, invalidationConsumerRegistered: true);

        await advisor.StartingAsync(AbortToken);

        logger.HasWarning("AutoRecoveryDelayTooLarge").Should().BeTrue();
    }

    [Fact]
    public async Task should_not_warn_when_auto_recovery_disabled_even_if_delay_exceeds_threshold()
    {
        var options = new HybridCacheOptions
        {
            EnableAutoRecovery = false,
            AutoRecoveryDelay = TimeSpan.FromMinutes(10),
        };

        var logger = new CapturingLogger();
        var advisor = new HybridCacheBestPracticesAdvisor(options, logger, invalidationConsumerRegistered: true);

        await advisor.StartingAsync(AbortToken);

        logger.HasWarning("AutoRecoveryDelayTooLarge").Should().BeFalse();
    }

    [Fact]
    public async Task should_not_warn_when_auto_recovery_delay_is_within_threshold()
    {
        var options = new HybridCacheOptions
        {
            EnableAutoRecovery = true,
            AutoRecoveryDelay = TimeSpan.FromMinutes(5), // exactly the threshold — not exceeded
        };

        var logger = new CapturingLogger();
        var advisor = new HybridCacheBestPracticesAdvisor(options, logger, invalidationConsumerRegistered: true);

        await advisor.StartingAsync(AbortToken);

        logger.HasWarning("AutoRecoveryDelayTooLarge").Should().BeFalse();
    }

    // ────────────────────────────────────────────────────────────
    // Check 2 — FailSafeMaxDuration <= Duration
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task should_warn_when_fail_safe_enabled_but_max_duration_not_beyond_duration()
    {
        var options = new HybridCacheOptions
        {
            DefaultEntryOptions = new CacheEntryOptions
            {
                Duration = TimeSpan.FromMinutes(10),
                IsFailSafeEnabled = true,
                FailSafeMaxDuration = TimeSpan.FromMinutes(10), // equal — no reserve
            },
        };

        var logger = new CapturingLogger();
        var advisor = new HybridCacheBestPracticesAdvisor(options, logger, invalidationConsumerRegistered: true);

        await advisor.StartingAsync(AbortToken);

        logger.HasWarning("FailSafeMaxDurationNotBeyondDuration").Should().BeTrue();
    }

    [Fact]
    public async Task should_warn_when_fail_safe_max_duration_less_than_duration()
    {
        var options = new HybridCacheOptions
        {
            DefaultEntryOptions = new CacheEntryOptions
            {
                Duration = TimeSpan.FromMinutes(10),
                IsFailSafeEnabled = true,
                FailSafeMaxDuration = TimeSpan.FromMinutes(5), // less — definitely no reserve
            },
        };

        var logger = new CapturingLogger();
        var advisor = new HybridCacheBestPracticesAdvisor(options, logger, invalidationConsumerRegistered: true);

        await advisor.StartingAsync(AbortToken);

        logger.HasWarning("FailSafeMaxDurationNotBeyondDuration").Should().BeTrue();
    }

    [Fact]
    public async Task should_not_warn_when_fail_safe_max_duration_exceeds_duration()
    {
        var options = new HybridCacheOptions
        {
            DefaultEntryOptions = new CacheEntryOptions
            {
                Duration = TimeSpan.FromMinutes(10),
                IsFailSafeEnabled = true,
                FailSafeMaxDuration = TimeSpan.FromHours(1), // proper reserve
            },
        };

        var logger = new CapturingLogger();
        var advisor = new HybridCacheBestPracticesAdvisor(options, logger, invalidationConsumerRegistered: true);

        await advisor.StartingAsync(AbortToken);

        logger.HasWarning("FailSafeMaxDurationNotBeyondDuration").Should().BeFalse();
    }

    [Fact]
    public async Task should_not_warn_when_fail_safe_disabled_even_if_max_duration_not_beyond()
    {
        var options = new HybridCacheOptions
        {
            DefaultEntryOptions = new CacheEntryOptions
            {
                Duration = TimeSpan.FromMinutes(10),
                IsFailSafeEnabled = false,
                FailSafeMaxDuration = TimeSpan.FromMinutes(5),
            },
        };

        var logger = new CapturingLogger();
        var advisor = new HybridCacheBestPracticesAdvisor(options, logger, invalidationConsumerRegistered: true);

        await advisor.StartingAsync(AbortToken);

        logger.HasWarning("FailSafeMaxDurationNotBeyondDuration").Should().BeFalse();
    }

    // ────────────────────────────────────────────────────────────
    // Check 3 — FactorySoftTimeout finite but IsFailSafeEnabled = false
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task should_warn_when_soft_timeout_set_but_fail_safe_disabled()
    {
        var options = new HybridCacheOptions
        {
            DefaultEntryOptions = new CacheEntryOptions
            {
                Duration = TimeSpan.FromMinutes(5),
                FactorySoftTimeout = TimeSpan.FromSeconds(500),
                IsFailSafeEnabled = false,
            },
        };

        var logger = new CapturingLogger();
        var advisor = new HybridCacheBestPracticesAdvisor(options, logger, invalidationConsumerRegistered: true);

        await advisor.StartingAsync(AbortToken);

        logger.HasWarning("FactorySoftTimeoutInertWithoutFailSafe").Should().BeTrue();
    }

    [Fact]
    public async Task should_not_warn_when_soft_timeout_set_and_fail_safe_enabled()
    {
        var options = new HybridCacheOptions
        {
            DefaultEntryOptions = new CacheEntryOptions
            {
                Duration = TimeSpan.FromMinutes(5),
                IsFailSafeEnabled = true,
                FailSafeMaxDuration = TimeSpan.FromHours(1),
                FactorySoftTimeout = TimeSpan.FromSeconds(500),
                BackgroundFactoryCeiling = TimeSpan.FromSeconds(30),
            },
        };

        var logger = new CapturingLogger();
        var advisor = new HybridCacheBestPracticesAdvisor(options, logger, invalidationConsumerRegistered: true);

        await advisor.StartingAsync(AbortToken);

        logger.HasWarning("FactorySoftTimeoutInertWithoutFailSafe").Should().BeFalse();
    }

    [Fact]
    public async Task should_not_warn_when_soft_timeout_is_infinite()
    {
        var options = new HybridCacheOptions
        {
            DefaultEntryOptions = new CacheEntryOptions
            {
                Duration = TimeSpan.FromMinutes(5),
                FactorySoftTimeout = Timeout.InfiniteTimeSpan,
                IsFailSafeEnabled = false,
            },
        };

        var logger = new CapturingLogger();
        var advisor = new HybridCacheBestPracticesAdvisor(options, logger, invalidationConsumerRegistered: true);

        await advisor.StartingAsync(AbortToken);

        logger.HasWarning("FactorySoftTimeoutInertWithoutFailSafe").Should().BeFalse();
    }

    // ────────────────────────────────────────────────────────────
    // Check 4 — EagerRefreshThreshold >= 0.95
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task should_warn_when_eager_refresh_threshold_at_or_above_limit()
    {
        var options = new HybridCacheOptions
        {
            DefaultEntryOptions = new CacheEntryOptions
            {
                Duration = TimeSpan.FromMinutes(5),
                EagerRefreshThreshold = 0.95f, // exactly at the limit
            },
        };

        var logger = new CapturingLogger();
        var advisor = new HybridCacheBestPracticesAdvisor(options, logger, invalidationConsumerRegistered: true);

        await advisor.StartingAsync(AbortToken);

        logger.HasWarning("EagerRefreshThresholdTooHigh").Should().BeTrue();
    }

    [Fact]
    public async Task should_warn_when_eager_refresh_threshold_above_limit()
    {
        var options = new HybridCacheOptions
        {
            DefaultEntryOptions = new CacheEntryOptions
            {
                Duration = TimeSpan.FromMinutes(5),
                EagerRefreshThreshold = 0.99f,
            },
        };

        var logger = new CapturingLogger();
        var advisor = new HybridCacheBestPracticesAdvisor(options, logger, invalidationConsumerRegistered: true);

        await advisor.StartingAsync(AbortToken);

        logger.HasWarning("EagerRefreshThresholdTooHigh").Should().BeTrue();
    }

    [Fact]
    public async Task should_not_warn_when_eager_refresh_threshold_below_limit()
    {
        var options = new HybridCacheOptions
        {
            DefaultEntryOptions = new CacheEntryOptions
            {
                Duration = TimeSpan.FromMinutes(5),
                EagerRefreshThreshold = 0.80f,
            },
        };

        var logger = new CapturingLogger();
        var advisor = new HybridCacheBestPracticesAdvisor(options, logger, invalidationConsumerRegistered: true);

        await advisor.StartingAsync(AbortToken);

        logger.HasWarning("EagerRefreshThresholdTooHigh").Should().BeFalse();
    }

    [Fact]
    public async Task should_not_warn_when_eager_refresh_threshold_not_set()
    {
        var options = new HybridCacheOptions
        {
            DefaultEntryOptions = new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) },
        };

        var logger = new CapturingLogger();
        var advisor = new HybridCacheBestPracticesAdvisor(options, logger, invalidationConsumerRegistered: true);

        await advisor.StartingAsync(AbortToken);

        logger.HasWarning("EagerRefreshThresholdTooHigh").Should().BeFalse();
    }

    // ────────────────────────────────────────────────────────────
    // Silence — no DefaultEntryOptions
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task should_emit_no_entry_option_warnings_when_default_entry_options_not_set()
    {
        var options = new HybridCacheOptions(); // DefaultEntryOptions is null

        var logger = new CapturingLogger();
        var advisor = new HybridCacheBestPracticesAdvisor(options, logger, invalidationConsumerRegistered: true);

        await advisor.StartingAsync(AbortToken);

        logger
            .Warnings()
            .Where(e =>
                e.Name
                    is "FailSafeMaxDurationNotBeyondDuration"
                        or "FactorySoftTimeoutInertWithoutFailSafe"
                        or "EagerRefreshThresholdTooHigh"
            )
            .Should()
            .BeEmpty();
    }

    // ────────────────────────────────────────────────────────────
    // Multiple warnings can fire together
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task should_emit_all_applicable_warnings_for_maximally_misconfigured_options()
    {
        var options = new HybridCacheOptions
        {
            EnableAutoRecovery = true,
            AutoRecoveryDelay = TimeSpan.FromHours(1), // check 1
            DefaultEntryOptions = new CacheEntryOptions
            {
                Duration = TimeSpan.FromMinutes(10),
                IsFailSafeEnabled = true,
                FailSafeMaxDuration = TimeSpan.FromMinutes(5), // check 2
                FactorySoftTimeout = Timeout.InfiniteTimeSpan, // not check 3 (infinite)
                EagerRefreshThreshold = 0.98f, // check 4
            },
        };

        var logger = new CapturingLogger();
        var advisor = new HybridCacheBestPracticesAdvisor(options, logger, invalidationConsumerRegistered: true);

        await advisor.StartingAsync(AbortToken);

        logger.HasWarning("AutoRecoveryDelayTooLarge").Should().BeTrue();
        logger.HasWarning("FailSafeMaxDurationNotBeyondDuration").Should().BeTrue();
        logger.HasWarning("EagerRefreshThresholdTooHigh").Should().BeTrue();
        // FactorySoftTimeoutInertWithoutFailSafe is NOT raised: IsFailSafeEnabled=true here
        logger.HasWarning("FactorySoftTimeoutInertWithoutFailSafe").Should().BeFalse();
    }

    // ────────────────────────────────────────────────────────────
    // Check 5 — no IConsume<CacheInvalidationMessage> consumer registered
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task should_warn_when_invalidation_consumer_is_not_registered()
    {
        var options = new HybridCacheOptions();

        var logger = new CapturingLogger();
        var advisor = new HybridCacheBestPracticesAdvisor(options, logger, invalidationConsumerRegistered: false);

        await advisor.StartingAsync(AbortToken);

        logger.HasWarning("InvalidationConsumerNotRegistered").Should().BeTrue();
    }

    [Fact]
    public async Task should_not_warn_when_invalidation_consumer_is_registered()
    {
        var options = new HybridCacheOptions();

        var logger = new CapturingLogger();
        var advisor = new HybridCacheBestPracticesAdvisor(options, logger, invalidationConsumerRegistered: true);

        await advisor.StartingAsync(AbortToken);

        logger.HasWarning("InvalidationConsumerNotRegistered").Should().BeFalse();
    }
}

/// <summary>
/// Minimal <see cref="ILogger{T}"/> capture for <see cref="HybridCacheBestPracticesAdvisor"/> tests.
/// Stores the <see cref="EventId"/> (name + id) for each log call so tests can assert on event names.
/// </summary>
internal sealed class CapturingLogger : ILogger<HybridCacheBestPracticesAdvisor>
{
    private readonly List<(LogLevel Level, EventId Event)> _entries = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    ) => _entries.Add((logLevel, eventId));

    public IEnumerable<EventId> Warnings() => _entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Event);

    public bool HasWarning(string eventName) =>
        _entries.Any(e =>
            e.Level == LogLevel.Warning && string.Equals(e.Event.Name, eventName, StringComparison.Ordinal)
        );
}
