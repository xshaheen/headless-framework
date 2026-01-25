// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.Dashboard;

namespace Tests;

public sealed class DashboardOptionsTests : TestBase
{
    [Fact]
    public void AllowAnonymousExplicit_should_default_to_true()
    {
        // given & when
        var options = new DashboardOptions();

        // then
        // NOTE: This is a security concern - defaults to true (anonymous access allowed)
        // Test documents current behavior which should be reviewed
        options.AllowAnonymousExplicit.Should().BeTrue();
    }

    [Fact]
    public void PathMatch_should_have_sensible_default()
    {
        // given & when
        var options = new DashboardOptions();

        // then
        options.PathMatch.Should().Be("/messaging");
    }

    [Fact]
    public void PathBase_should_default_to_empty()
    {
        // given & when
        var options = new DashboardOptions();

        // then
        options.PathBase.Should().BeEmpty();
    }

    [Fact]
    public void StatsPollingInterval_should_have_default_value()
    {
        // given & when
        var options = new DashboardOptions();

        // then
        options.StatsPollingInterval.Should().Be(2000);
    }

    [Fact]
    public void AuthorizationPolicy_should_default_to_null()
    {
        // given & when
        var options = new DashboardOptions();

        // then
        options.AuthorizationPolicy.Should().BeNull();
    }

    [Fact]
    public void should_allow_custom_PathMatch()
    {
        // given & when
        var options = new DashboardOptions { PathMatch = "/custom/dashboard" };

        // then
        options.PathMatch.Should().Be("/custom/dashboard");
    }

    [Fact]
    public void should_allow_custom_PathBase()
    {
        // given & when
        var options = new DashboardOptions { PathBase = "/api/v1" };

        // then
        options.PathBase.Should().Be("/api/v1");
    }

    [Fact]
    public void should_allow_custom_StatsPollingInterval()
    {
        // given & when
        var options = new DashboardOptions { StatsPollingInterval = 5000 };

        // then
        options.StatsPollingInterval.Should().Be(5000);
    }

    [Fact]
    public void should_allow_disabling_anonymous_access()
    {
        // given & when
        var options = new DashboardOptions
        {
            AllowAnonymousExplicit = false,
            AuthorizationPolicy = "AdminOnly",
        };

        // then
        options.AllowAnonymousExplicit.Should().BeFalse();
        options.AuthorizationPolicy.Should().Be("AdminOnly");
    }
}
