// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard.Authentication;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupDashboardAuthenticationTests : TestBase
{
    [Fact]
    public void add_dashboard_authentication_registers_auth_service_and_config()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDashboardAuthentication(cfg =>
        {
            cfg.Mode = AuthMode.ApiKey;
            cfg.ApiKey = "secret";
        });

        using var provider = services.BuildServiceProvider();

        var config = provider.GetService<AuthConfig>();
        config.Should().NotBeNull();
        config!.Mode.Should().Be(AuthMode.ApiKey);

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetService<IAuthService>().Should().BeOfType<AuthService>();
    }

    [Fact]
    public void add_dashboard_authentication_validates_configuration_on_resolve()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Basic mode without BasicCredentials is invalid.
        services.AddDashboardAuthentication(cfg => cfg.Mode = AuthMode.Basic);

        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<AuthConfig>();

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void add_dashboard_authentication_throws_for_api_key_mode_without_key()
    {
        var act = () => _ResolveConfig(cfg => cfg.Mode = AuthMode.ApiKey);

        act.Should().Throw<OptionsValidationException>().WithMessage("*ApiKey*");
    }

    [Fact]
    public void add_dashboard_authentication_throws_for_custom_mode_without_validator()
    {
        var act = () => _ResolveConfig(cfg => cfg.Mode = AuthMode.Custom);

        act.Should().Throw<OptionsValidationException>().WithMessage("*CustomValidator*");
    }

    [Theory]
    [InlineData(AuthMode.None)]
    [InlineData(AuthMode.Host)]
    public void add_dashboard_authentication_passes_for_credentialless_modes(AuthMode mode)
    {
        var act = () => _ResolveConfig(cfg => cfg.Mode = mode);

        act.Should().NotThrow();
    }

    [Fact]
    public void add_dashboard_authentication_passes_for_basic_with_credentials()
    {
        var act = () =>
            _ResolveConfig(cfg =>
            {
                cfg.Mode = AuthMode.Basic;
                cfg.BasicCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:pass"));
            });

        act.Should().NotThrow();
    }

    [Fact]
    public void add_dashboard_authentication_passes_for_custom_with_validator()
    {
        var act = () =>
            _ResolveConfig(cfg =>
            {
                cfg.Mode = AuthMode.Custom;
                cfg.CustomValidator = (_, _) => true;
            });

        act.Should().NotThrow();
    }

    private static void _ResolveConfig(Action<AuthConfig> setupAction)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDashboardAuthentication(setupAction);

        using var provider = services.BuildServiceProvider();

        // Resolving the AuthConfig value runs the FluentValidation options pipeline —
        // the single validation point for every construction path.
        provider.GetRequiredService<AuthConfig>();
    }
}
