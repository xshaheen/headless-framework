// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard.Authentication;
using Headless.Testing.Tests;

namespace Tests;

public sealed class AuthConfigTests : TestBase
{
    [Fact]
    public void defaults_to_none_mode()
    {
        var config = new AuthConfig();

        config.Mode.Should().Be(AuthMode.None);
        config.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void is_enabled_when_mode_is_not_none()
    {
        var config = new AuthConfig { Mode = AuthMode.Basic };
        config.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void session_timeout_defaults_to_60()
    {
        var config = new AuthConfig();
        config.SessionTimeoutMinutes.Should().Be(60);
    }
}
