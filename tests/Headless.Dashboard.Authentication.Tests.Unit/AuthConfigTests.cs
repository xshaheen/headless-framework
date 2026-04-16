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

    [Fact]
    public void validate_throws_for_basic_without_credentials()
    {
        var config = new AuthConfig { Mode = AuthMode.Basic };

        var act = () => config.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*BasicCredentials*");
    }

    [Fact]
    public void validate_throws_for_apikey_without_key()
    {
        var config = new AuthConfig { Mode = AuthMode.ApiKey };

        var act = () => config.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*ApiKey*");
    }

    [Fact]
    public void validate_throws_for_custom_without_validator()
    {
        var config = new AuthConfig { Mode = AuthMode.Custom };

        var act = () => config.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*CustomValidator*");
    }

    [Fact]
    public void validate_passes_for_none()
    {
        var config = new AuthConfig { Mode = AuthMode.None };

        var act = () => config.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void validate_passes_for_host()
    {
        var config = new AuthConfig { Mode = AuthMode.Host };

        var act = () => config.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void validate_passes_for_basic_with_credentials()
    {
        var config = new AuthConfig
        {
            Mode = AuthMode.Basic,
            BasicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:pass")),
        };

        var act = () => config.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void validate_passes_for_apikey_with_key()
    {
        var config = new AuthConfig { Mode = AuthMode.ApiKey, ApiKey = "test-api-key" };

        var act = () => config.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void validate_passes_for_custom_with_validator()
    {
        var config = new AuthConfig { Mode = AuthMode.Custom, CustomValidator = (_, _) => true };

        var act = () => config.Validate();

        act.Should().NotThrow();
    }
}
