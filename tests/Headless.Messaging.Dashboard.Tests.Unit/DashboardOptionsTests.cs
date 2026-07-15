// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard.Authentication;
using Headless.Messaging.Dashboard;
using Headless.Testing.Tests;

namespace Tests;

public sealed class DashboardOptionsTests : TestBase
{
    [Fact]
    public void should_be_none_when_default_auth_mode()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder();

        // then - defaults to None (no auth)
        builder.Auth.Mode.Should().Be(AuthMode.None);
    }

    [Fact]
    public void should_have_sensible_default_when_base_path()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder();

        // then
        builder.BasePath.Should().Be("/messaging");
    }

    [Fact]
    public void should_have_default_value_when_stats_polling_interval()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder();

        // then
        builder.StatsPollingInterval.Should().Be(2000);
    }

    [Fact]
    public void should_allow_custom_base_path()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder().SetBasePath("/custom/dashboard");

        // then
        builder.BasePath.Should().Be("/custom/dashboard");
    }

    [Fact]
    public void should_allow_custom_stats_polling_interval()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder().SetStatsPollingInterval(5000);

        // then
        builder.StatsPollingInterval.Should().Be(5000);
    }

    [Fact]
    public void should_set_mode_to_none_when_with_no_auth()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder().WithNoAuth();

        // then
        builder.Auth.Mode.Should().Be(AuthMode.None);
        builder.Auth.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void should_set_mode_and_credentials_when_with_basic_auth()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder().WithBasicAuth("admin", "password");

        // then
        builder.Auth.Mode.Should().Be(AuthMode.Basic);
        builder.Auth.IsEnabled.Should().BeTrue();
        builder.Auth.BasicCredentials.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void should_set_mode_and_key_when_with_api_key()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder().WithApiKey("my-secret-key");

        // then
        builder.Auth.Mode.Should().Be(AuthMode.ApiKey);
        builder.Auth.IsEnabled.Should().BeTrue();
        builder.Auth.ApiKey.Should().Be("my-secret-key");
    }

    [Fact]
    public void should_set_mode_and_policy_when_with_host_authentication()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder().WithHostAuthentication("AdminOnly");

        // then
        builder.Auth.Mode.Should().Be(AuthMode.Host);
        builder.Auth.IsEnabled.Should().BeTrue();
        builder.Auth.HostAuthorizationPolicy.Should().Be("AdminOnly");
    }

    [Fact]
    public void should_use_default_when_with_host_authentication_without_policy()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder().WithHostAuthentication();

        // then
        builder.Auth.Mode.Should().Be(AuthMode.Host);
        builder.Auth.HostAuthorizationPolicy.Should().BeNull();
    }

    [Fact]
    public void should_set_mode_and_validator_when_with_custom_auth()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder().WithCustomAuth(
            (token, _) => string.Equals(token, "valid", StringComparison.Ordinal)
        );

        // then
        builder.Auth.Mode.Should().Be(AuthMode.Custom);
        builder.Auth.IsEnabled.Should().BeTrue();
        builder.Auth.CustomValidator.Should().NotBeNull();
    }

    [Fact]
    public void should_set_timeout_when_with_session_timeout()
    {
        // given & when
        var builder = new MessagingDashboardOptionsBuilder().WithSessionTimeout(30);

        // then
        builder.Auth.SessionTimeoutMinutes.Should().Be(30);
    }

    [Fact]
    public void should_be_chainable_when_fluent_api()
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
    public void should_throw_for_basic_without_credentials_when_validate()
    {
        // given
        var builder = new MessagingDashboardOptionsBuilder().WithBasicAuth("admin", "password");
        builder.Auth.BasicCredentials = null;

        // when
        var act = () => builder.Validate();

        // then
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_throw_for_api_key_without_key_when_validate()
    {
        // given
        var builder = new MessagingDashboardOptionsBuilder().WithApiKey("temp-key");
        builder.Auth.ApiKey = null;

        // when
        var act = () => builder.Validate();

        // then
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_throw_for_custom_without_validator_when_validate()
    {
        // given
        var builder = new MessagingDashboardOptionsBuilder().WithCustomAuth((_, _) => true);
        builder.Auth.CustomValidator = null;

        // when
        var act = () => builder.Validate();

        // then
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_throw_when_validate_no_auth_mode_configured()
    {
        // given — a builder where no WithXxx auth method (not even WithNoAuth) was ever called
        var builder = new MessagingDashboardOptionsBuilder();

        // when
        var act = () => builder.Validate();

        // then
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_pass_when_validate_explicitly_opting_out_with_with_no_auth()
    {
        // given
        var builder = new MessagingDashboardOptionsBuilder().WithNoAuth();

        // when
        var act = () => builder.Validate();

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_pass_for_properly_configured_basic_auth_when_validate()
    {
        // given
        var builder = new MessagingDashboardOptionsBuilder().WithBasicAuth("admin", "password");

        // when
        var act = () => builder.Validate();

        // then
        act.Should().NotThrow();
    }
}
