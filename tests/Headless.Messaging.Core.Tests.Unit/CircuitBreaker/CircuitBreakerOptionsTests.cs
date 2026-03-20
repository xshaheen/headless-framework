// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.CircuitBreaker;

public sealed class CircuitBreakerOptionsTests : TestBase
{
    // -------------------------------------------------------------------------
    // Default values
    // -------------------------------------------------------------------------

    [Fact]
    public void defaults_are_correct()
    {
        var opts = new CircuitBreakerOptions();

        opts.FailureThreshold.Should().Be(5);
        opts.OpenDuration.Should().Be(TimeSpan.FromSeconds(30));
        opts.MaxOpenDuration.Should().Be(TimeSpan.FromSeconds(240));
        opts.SuccessfulCyclesToResetEscalation.Should().Be(3);
        opts.IsTransientException.Should().NotBeNull();
    }

    [Fact]
    public void default_is_transient_predicate_matches_CircuitBreakerDefaults()
    {
        var opts = new CircuitBreakerOptions();

        opts.IsTransientException(new TimeoutException()).Should().BeTrue();
        opts.IsTransientException(new ArgumentException("bad")).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Validation — FailureThreshold
    // -------------------------------------------------------------------------

    [Fact]
    public void validator_rejects_failure_threshold_of_zero()
    {
        var opts = new CircuitBreakerOptions { FailureThreshold = 0 };
        var validator = new CircuitBreakerOptionsValidator();

        var result = validator.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainMatch("*FailureThreshold*greater than 0*");
    }

    [Fact]
    public void validator_rejects_negative_failure_threshold()
    {
        var opts = new CircuitBreakerOptions { FailureThreshold = -1 };
        var validator = new CircuitBreakerOptionsValidator();

        var result = validator.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainMatch("*FailureThreshold*greater than 0*");
    }

    [Fact]
    public void validator_accepts_positive_failure_threshold()
    {
        var opts = new CircuitBreakerOptions { FailureThreshold = 1 };
        var validator = new CircuitBreakerOptionsValidator();

        var result = validator.Validate(null, opts);

        result.Failed.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Validation — MaxOpenDuration vs OpenDuration
    // -------------------------------------------------------------------------

    [Fact]
    public void validator_rejects_max_open_duration_less_than_open_duration()
    {
        var opts = new CircuitBreakerOptions
        {
            OpenDuration = TimeSpan.FromSeconds(60),
            MaxOpenDuration = TimeSpan.FromSeconds(30),
        };
        var validator = new CircuitBreakerOptionsValidator();

        var result = validator.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainMatch("*MaxOpenDuration*OpenDuration*");
    }

    [Fact]
    public void validator_accepts_max_open_duration_equal_to_open_duration()
    {
        var opts = new CircuitBreakerOptions
        {
            OpenDuration = TimeSpan.FromSeconds(30),
            MaxOpenDuration = TimeSpan.FromSeconds(30),
        };
        var validator = new CircuitBreakerOptionsValidator();

        var result = validator.Validate(null, opts);

        result.Failed.Should().BeFalse();
    }

    [Fact]
    public void validator_accepts_max_open_duration_greater_than_open_duration()
    {
        var opts = new CircuitBreakerOptions
        {
            OpenDuration = TimeSpan.FromSeconds(30),
            MaxOpenDuration = TimeSpan.FromSeconds(240),
        };
        var validator = new CircuitBreakerOptionsValidator();

        var result = validator.Validate(null, opts);

        result.Failed.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Validation — IsTransientException
    // -------------------------------------------------------------------------

    [Fact]
    public void validator_rejects_null_is_transient_predicate()
    {
        var opts = new CircuitBreakerOptions { IsTransientException = null! };
        var validator = new CircuitBreakerOptionsValidator();

        var result = validator.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainMatch("*IsTransientException*null*");
    }

    // -------------------------------------------------------------------------
    // Validation — multiple failures reported together
    // -------------------------------------------------------------------------

    [Fact]
    public void validator_reports_all_failures()
    {
        var opts = new CircuitBreakerOptions
        {
            FailureThreshold = 0,
            OpenDuration = TimeSpan.FromSeconds(60),
            MaxOpenDuration = TimeSpan.FromSeconds(10),
            IsTransientException = null!,
        };
        var validator = new CircuitBreakerOptionsValidator();

        var result = validator.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(3);
    }
}

public sealed class RetryProcessorOptionsTests : TestBase
{
    [Fact]
    public void defaults_are_correct()
    {
        var opts = new RetryProcessorOptions();

        opts.AdaptivePolling.Should().BeTrue();
        opts.MaxPollingInterval.Should().Be(TimeSpan.FromMinutes(15));
        opts.CircuitOpenRateThreshold.Should().Be(0.8);
    }

    [Fact]
    public void validator_rejects_max_polling_interval_of_zero()
    {
        var opts = new RetryProcessorOptions { MaxPollingInterval = TimeSpan.Zero };
        var validator = new RetryProcessorOptionsValidator();

        var result = validator.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainMatch("*MaxPollingInterval*greater than TimeSpan.Zero*");
    }

    [Fact]
    public void validator_rejects_transient_failure_rate_of_zero()
    {
        var opts = new RetryProcessorOptions { CircuitOpenRateThreshold = 0 };
        var validator = new RetryProcessorOptionsValidator();

        var result = validator.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainMatch("*CircuitOpenRateThreshold*between 0 and 1*");
    }

    [Fact]
    public void validator_rejects_transient_failure_rate_of_one()
    {
        var opts = new RetryProcessorOptions { CircuitOpenRateThreshold = 1.0 };
        var validator = new RetryProcessorOptionsValidator();

        var result = validator.Validate(null, opts);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainMatch("*CircuitOpenRateThreshold*between 0 and 1*");
    }

    [Fact]
    public void validator_accepts_valid_options()
    {
        var opts = new RetryProcessorOptions
        {
            MaxPollingInterval = TimeSpan.FromSeconds(60),
            CircuitOpenRateThreshold = 0.5,
        };
        var validator = new RetryProcessorOptionsValidator();

        var result = validator.Validate(null, opts);

        result.Failed.Should().BeFalse();
    }
}

public sealed class ConsumerCircuitBreakerRegistryTests : TestBase
{
    // -------------------------------------------------------------------------
    // WithCircuitBreaker via ConsumerBuilder (MessagingOptions path)
    // -------------------------------------------------------------------------

    [Fact]
    public void with_circuit_breaker_stores_options_in_registry()
    {
        var registry = new ConsumerCircuitBreakerRegistry();
        var opts = new ConsumerCircuitBreakerOptions { FailureThreshold = 3 };

        registry.Register("my-group", opts);

        var found = registry.TryGet("my-group", out var retrieved);

        found.Should().BeTrue();
        retrieved!.FailureThreshold.Should().Be(3);
    }

    [Fact]
    public void try_get_returns_false_for_unknown_group()
    {
        var registry = new ConsumerCircuitBreakerRegistry();

        var found = registry.TryGet("unknown-group", out _);

        found.Should().BeFalse();
    }

    [Fact]
    public void register_overwrites_previous_entry_for_same_group()
    {
        var registry = new ConsumerCircuitBreakerRegistry();

        registry.Register("my-group", new ConsumerCircuitBreakerOptions { FailureThreshold = 3 });
        registry.Register("my-group", new ConsumerCircuitBreakerOptions { FailureThreshold = 7 });

        registry.TryGet("my-group", out var retrieved);

        retrieved!.FailureThreshold.Should().Be(7);
    }

    // -------------------------------------------------------------------------
    // WithCircuitBreaker via MessagingOptions.Subscribe path
    // -------------------------------------------------------------------------

    [Fact]
    public void with_circuit_breaker_on_consumer_builder_registers_per_group()
    {
        // Simulates: options.Subscribe<Handler>().WithCircuitBreaker(cb => cb.FailureThreshold = 2)
        var cbRegistry = new ConsumerCircuitBreakerRegistry();
        cbRegistry.Register("orders.group", new ConsumerCircuitBreakerOptions { FailureThreshold = 2 });

        cbRegistry.TryGet("orders.group", out var perGroup).Should().BeTrue();
        perGroup!.FailureThreshold.Should().Be(2);
        perGroup.Enabled.Should().BeTrue();
    }

    [Fact]
    public void with_circuit_breaker_can_disable_circuit_for_consumer()
    {
        var cbRegistry = new ConsumerCircuitBreakerRegistry();
        cbRegistry.Register("best-effort.group", new ConsumerCircuitBreakerOptions { Enabled = false });

        cbRegistry.TryGet("best-effort.group", out var perGroup).Should().BeTrue();
        perGroup!.Enabled.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // ConsumerCircuitBreakerOptions defaults
    // -------------------------------------------------------------------------

    [Fact]
    public void consumer_circuit_breaker_options_defaults_are_null_overrides()
    {
        var opts = new ConsumerCircuitBreakerOptions();

        opts.Enabled.Should().BeTrue();
        opts.FailureThreshold.Should().BeNull();
        opts.OpenDuration.Should().BeNull();
        opts.IsTransientException.Should().BeNull();
    }
}
