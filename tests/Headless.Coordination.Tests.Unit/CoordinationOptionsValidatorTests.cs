// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Testing.Tests;

namespace Tests;

public sealed class CoordinationOptionsValidatorTests : TestBase
{
    private readonly CoordinationOptionsValidator _sut = new();

    [Fact]
    public void should_accept_valid_thresholds_and_retention_window()
    {
        // given
        var options = new CoordinationOptions
        {
            HeartbeatInterval = TimeSpan.FromSeconds(5),
            SuspicionThreshold = TimeSpan.FromSeconds(15),
            DeadThreshold = TimeSpan.FromSeconds(30),
            DeadRetentionWindow = TimeSpan.FromSeconds(20),
        };

        // when
        var result = _sut.Validate(options);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 15, 30, 20, nameof(CoordinationOptions.HeartbeatInterval))]
    [InlineData(15, 15, 30, 30, nameof(CoordinationOptions.HeartbeatInterval))]
    [InlineData(5, 30, 30, 20, nameof(CoordinationOptions.SuspicionThreshold))]
    [InlineData(5, 35, 30, 20, nameof(CoordinationOptions.SuspicionThreshold))]
    [InlineData(5, 15, 30, 9, nameof(CoordinationOptions.DeadRetentionWindow))]
    // Strictly ordered but one missed beat from death: DeadThreshold must be >= 3x HeartbeatInterval.
    [InlineData(14, 15, 16, 30, nameof(CoordinationOptions.DeadThreshold))]
    [InlineData(11, 15, 30, 30, nameof(CoordinationOptions.DeadThreshold))]
    public void should_reject_invalid_threshold_triangle(
        int heartbeatSeconds,
        int suspicionSeconds,
        int deadSeconds,
        int retentionSeconds,
        string propertyName
    )
    {
        // given
        var options = new CoordinationOptions
        {
            HeartbeatInterval = TimeSpan.FromSeconds(heartbeatSeconds),
            SuspicionThreshold = TimeSpan.FromSeconds(suspicionSeconds),
            DeadThreshold = TimeSpan.FromSeconds(deadSeconds),
            DeadRetentionWindow = TimeSpan.FromSeconds(retentionSeconds),
        };

        // when
        var result = _sut.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == propertyName);
    }

    [Fact]
    public void should_accept_the_default_self_fence_margin()
    {
        _sut.Validate(new CoordinationOptions()).IsValid.Should().BeTrue();
    }

    [Theory]
    // 1.2s threshold on 200ms beats: a fixed 5s margin would leave a negative window.
    [InlineData(200, 1200)]
    // The tightest valid triangle: DeadThreshold exactly three beats.
    [InlineData(1000, 3000)]
    public void should_accept_a_short_dead_threshold_when_the_margin_is_left_to_its_derived_default(
        int heartbeatMilliseconds,
        int deadMilliseconds
    )
    {
        var options = new CoordinationOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(heartbeatMilliseconds),
            SuspicionThreshold = TimeSpan.FromMilliseconds(heartbeatMilliseconds * 2),
            DeadThreshold = TimeSpan.FromMilliseconds(deadMilliseconds),
            DeadRetentionWindow = TimeSpan.FromMilliseconds(deadMilliseconds),
        };

        _sut.Validate(options).IsValid.Should().BeTrue();
    }

    [Theory]
    // Negative margin.
    [InlineData(-1, 1000, 3000)]
    // DeadThreshold 30s minus a 21s margin leaves 9s, under two 5s beats.
    [InlineData(21_000, 5000, 30_000)]
    // A 1.2s DeadThreshold cannot carry an explicit 5s margin.
    [InlineData(5000, 200, 1200)]
    public void should_reject_a_self_fence_margin_that_leaves_less_than_two_beats(
        int marginMilliseconds,
        int heartbeatMilliseconds,
        int deadMilliseconds
    )
    {
        // given
        var options = new CoordinationOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(heartbeatMilliseconds),
            SuspicionThreshold = TimeSpan.FromMilliseconds(heartbeatMilliseconds * 2),
            DeadThreshold = TimeSpan.FromMilliseconds(deadMilliseconds),
            DeadRetentionWindow = TimeSpan.FromMilliseconds(deadMilliseconds),
            SelfFenceMargin = TimeSpan.FromMilliseconds(marginMilliseconds),
        };

        // when
        var result = _sut.Validate(options);

        // then
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CoordinationOptions.SelfFenceMargin));
    }

    [Fact]
    public void should_accept_a_margin_that_leaves_exactly_two_beats()
    {
        var options = new CoordinationOptions
        {
            HeartbeatInterval = TimeSpan.FromSeconds(5),
            SuspicionThreshold = TimeSpan.FromSeconds(15),
            DeadThreshold = TimeSpan.FromSeconds(30),
            SelfFenceMargin = TimeSpan.FromSeconds(20),
        };

        _sut.Validate(options).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("cluster one")]
    [InlineData("cluster/one")]
    [InlineData("cluster{one}")]
    public void should_reject_cluster_name_outside_safe_identifier_set(string clusterName)
    {
        // given
        var options = new CoordinationOptions { ClusterName = clusterName };

        // when
        var result = _sut.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.PropertyName == nameof(CoordinationOptions.ClusterName));
    }

    [Fact]
    public void should_reject_a_cluster_name_longer_than_the_maximum()
    {
        var atLimit = new CoordinationOptions
        {
            ClusterName = new string('c', CoordinationOptions.ClusterNameMaxLength),
        };
        var overLimit = new CoordinationOptions
        {
            ClusterName = new string('c', CoordinationOptions.ClusterNameMaxLength + 1),
        };

        _sut.Validate(atLimit).IsValid.Should().BeTrue();
        _sut.Validate(overLimit)
            .Errors.Should()
            .ContainSingle(e => e.PropertyName == nameof(CoordinationOptions.ClusterName));
    }
}
