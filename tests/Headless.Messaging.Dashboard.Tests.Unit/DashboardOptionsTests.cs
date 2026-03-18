// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard.Authentication;
using Headless.Messaging.Dashboard;
using Headless.Testing.Tests;

namespace Tests;

public sealed class DashboardOptionsTests : TestBase
{
    [Fact]
    public void default_auth_mode_should_be_None()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder();

        // then - defaults to None (no auth)
        builder.Auth.Mode.Should().Be(AuthMode.None);
    }

    [Fact]
    public void BasePath_should_have_sensible_default()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder();

        // then
        builder.BasePath.Should().Be("/messaging");
    }

    [Fact]
    public void StatsPollingInterval_should_have_default_value()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder();

        // then
        builder.StatsPollingInterval.Should().Be(2000);
    }

    [Fact]
    public void should_allow_custom_BasePath()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder().SetBasePath("/custom/dashboard");

        // then
        builder.BasePath.Should().Be("/custom/dashboard");
    }

    [Fact]
    public void should_allow_custom_StatsPollingInterval()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder().SetStatsPollingInterval(5000);

        // then
        builder.StatsPollingInterval.Should().Be(5000);
    }

    [Fact]
    public void WithNoAuth_should_set_mode_to_None()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder().WithNoAuth();

        // then
        builder.Auth.Mode.Should().Be(AuthMode.None);
        builder.Auth.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void WithBasicAuth_should_set_mode_and_credentials()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder().WithBasicAuth("admin", "password");

        // then
        builder.Auth.Mode.Should().Be(AuthMode.Basic);
        builder.Auth.IsEnabled.Should().BeTrue();
        builder.Auth.BasicCredentials.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void WithApiKey_should_set_mode_and_key()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder().WithApiKey("my-secret-key");

        // then
        builder.Auth.Mode.Should().Be(AuthMode.ApiKey);
        builder.Auth.IsEnabled.Should().BeTrue();
        builder.Auth.ApiKey.Should().Be("my-secret-key");
    }

    [Fact]
    public void WithHostAuthentication_should_set_mode_and_policy()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder().WithHostAuthentication("AdminOnly");

        // then
        builder.Auth.Mode.Should().Be(AuthMode.Host);
        builder.Auth.IsEnabled.Should().BeTrue();
        builder.Auth.HostAuthorizationPolicy.Should().Be("AdminOnly");
    }

    [Fact]
    public void WithHostAuthentication_without_policy_should_use_default()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder().WithHostAuthentication();

        // then
        builder.Auth.Mode.Should().Be(AuthMode.Host);
        builder.Auth.HostAuthorizationPolicy.Should().BeNull();
    }

    [Fact]
    public void WithCustomAuth_should_set_mode_and_validator()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder().WithCustomAuth((token, _) => token == "valid");

        // then
        builder.Auth.Mode.Should().Be(AuthMode.Custom);
        builder.Auth.IsEnabled.Should().BeTrue();
        builder.Auth.CustomValidator.Should().NotBeNull();
    }

    [Fact]
    public void WithSessionTimeout_should_set_timeout()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder().WithSessionTimeout(30);

        // then
        builder.Auth.SessionTimeoutMinutes.Should().Be(30);
    }

    [Fact]
    public void fluent_api_should_be_chainable()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder()
            .SetBasePath("/custom")
            .SetStatsPollingInterval(3000)
            .WithBasicAuth("admin", "pass")
            .WithSessionTimeout(120);

        // then
        builder.BasePath.Should().Be("/custom");
        builder.StatsPollingInterval.Should().Be(3000);
        builder.Auth.Mode.Should().Be(AuthMode.Basic);
        builder.Auth.SessionTimeoutMinutes.Should().Be(120);
    }

    [Fact]
    public void Validate_should_throw_for_Basic_without_credentials()
    {
        // given
        var builder = new MessagingDashboardOptionsBuilder();
        builder.Auth.Mode = AuthMode.Basic;
        builder.Auth.BasicCredentials = null;

        // when
        var act = () => builder.Validate();

        // then
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validate_should_throw_for_ApiKey_without_key()
    {
        // given
        var builder = new MessagingDashboardOptionsBuilder();
        builder.Auth.Mode = AuthMode.ApiKey;
        builder.Auth.ApiKey = null;

        // when
        var act = () => builder.Validate();

        // then
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validate_should_throw_for_Custom_without_validator()
    {
        // given
        var builder = new MessagingDashboardOptionsBuilder();
        builder.Auth.Mode = AuthMode.Custom;
        builder.Auth.CustomValidator = null;

        // when
        var act = () => builder.Validate();

        // then
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Validate_should_pass_for_properly_configured_BasicAuth()
    {
        // given
        var builder = new MessagingDashboardOptionsBuilder().WithBasicAuth("admin", "password");

        // when
        var act = () => builder.Validate();

        // then
        act.Should().NotThrow();
    }
}
