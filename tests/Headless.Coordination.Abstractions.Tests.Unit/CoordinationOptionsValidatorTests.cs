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
}
