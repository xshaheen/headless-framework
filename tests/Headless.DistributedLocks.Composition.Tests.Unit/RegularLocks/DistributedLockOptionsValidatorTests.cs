// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;

namespace Tests.RegularLocks;

public sealed class DistributedLockOptionsValidatorTests : TestBase
{
    private readonly DistributedLockOptionsValidator _validator = new();

    [Fact]
    public void should_validate_defaults()
    {
        // when
        var result = _validator.Validate(new DistributedLockOptions());

        // then
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.5)]
    public void should_validate_cadence_fraction_boundaries(double fraction)
    {
        // given
        var options = new DistributedLockOptions
        {
            PollingCadenceFraction = fraction,
            AutoExtensionCadenceFraction = fraction,
        };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0.09)]
    [InlineData(0.51)]
    public void should_reject_cadence_fractions_outside_bounds(double fraction)
    {
        // given
        var options = new DistributedLockOptions
        {
            PollingCadenceFraction = fraction,
            AutoExtensionCadenceFraction = fraction,
        };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(DistributedLockOptions.PollingCadenceFraction));
        result
            .Errors.Should()
            .Contain(e => e.PropertyName == nameof(DistributedLockOptions.AutoExtensionCadenceFraction));
    }

    [Fact]
    public void should_have_writer_waiting_marker_ttl_default_within_bounds()
    {
        // given
        var options = new DistributedLockOptions();

        // then
        options.WriterWaitingMarkerTtl.Should().BeGreaterThan(TimeSpan.Zero);
        options.WriterWaitingMarkerTtl.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(5));
        _validator.Validate(options).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_reject_non_positive_writer_waiting_marker_ttl(int seconds)
    {
        // given
        var options = new DistributedLockOptions { WriterWaitingMarkerTtl = TimeSpan.FromSeconds(seconds) };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(DistributedLockOptions.WriterWaitingMarkerTtl));
    }

    [Fact]
    public void should_reject_writer_waiting_marker_ttl_above_five_minutes()
    {
        // given
        var options = new DistributedLockOptions { WriterWaitingMarkerTtl = TimeSpan.FromMinutes(6) };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(DistributedLockOptions.WriterWaitingMarkerTtl));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_reject_invalid_max_resource_name_length(int length)
    {
        var options = new DistributedLockOptions { MaxResourceNameLength = length };
        var result = _validator.Validate(options);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(DistributedLockOptions.MaxResourceNameLength));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_reject_invalid_max_waiters_per_resource(int maxWaiters)
    {
        var options = new DistributedLockOptions { MaxWaitersPerResource = maxWaiters };
        var result = _validator.Validate(options);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(DistributedLockOptions.MaxWaitersPerResource));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_reject_invalid_max_concurrent_waiting_resources(int maxWaiting)
    {
        var options = new DistributedLockOptions { MaxConcurrentWaitingResources = maxWaiting };
        var result = _validator.Validate(options);
        result.IsValid.Should().BeFalse();
        result
            .Errors.Should()
            .Contain(e => e.PropertyName == nameof(DistributedLockOptions.MaxConcurrentWaitingResources));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10_000)]
    public void should_validate_boundary_max_resource_name_length(int length)
    {
        var options = new DistributedLockOptions { MaxResourceNameLength = length };
        var result = _validator.Validate(options);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100_000)]
    public void should_validate_boundary_max_waiters_per_resource(int maxWaiters)
    {
        var options = new DistributedLockOptions { MaxWaitersPerResource = maxWaiters };
        var result = _validator.Validate(options);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1_000_000)]
    public void should_validate_boundary_max_concurrent_waiting_resources(int maxWaiting)
    {
        var options = new DistributedLockOptions { MaxConcurrentWaitingResources = maxWaiting };
        var result = _validator.Validate(options);
        result.IsValid.Should().BeTrue();
    }
}
