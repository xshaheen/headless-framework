// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard.Authentication;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupDashboardAuthenticationTests : TestBase
{
    private const string _Name = "dashboard-a";
    private const string _OtherName = "dashboard-b";

    [Fact]
    public void add_dashboard_authentication_registers_a_keyed_auth_service_for_the_named_config()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDashboardAuthentication(
            _Name,
            cfg =>
            {
                cfg.Mode = AuthMode.ApiKey;
                cfg.ApiKey = "secret";
            }
        );

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var authService = scope.ServiceProvider.GetRequiredKeyedService<IAuthService>(_Name);
        authService.Should().BeOfType<AuthService>();
        authService.GetAuthInfo().Mode.Should().Be(AuthMode.ApiKey);
        scope.ServiceProvider.GetService<IAuthService>().Should().BeNull();
    }

    [Fact]
    public async Task two_named_registrations_keep_independent_modes_and_credentials()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDashboardAuthentication(_Name, cfg => cfg.Mode = AuthMode.None);
        services.AddDashboardAuthentication(
            _OtherName,
            cfg =>
            {
                cfg.Mode = AuthMode.Basic;
                cfg.BasicCredentials = "admin:pass".ToBase64();
            }
        );

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var first = scope.ServiceProvider.GetRequiredKeyedService<IAuthService>(_Name);
        var second = scope.ServiceProvider.GetRequiredKeyedService<IAuthService>(_OtherName);

        first.GetAuthInfo().Mode.Should().Be(AuthMode.None);
        second.GetAuthInfo().Mode.Should().Be(AuthMode.Basic);
        (await first.AuthenticateAsync(new DefaultHttpContext(), AbortToken)).IsAuthenticated.Should().BeTrue();
        (await second.AuthenticateAsync(new DefaultHttpContext(), AbortToken)).IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void add_dashboard_authentication_keeps_a_keyed_auth_service_registered_first()
    {
        var replacement = Substitute.For<IAuthService>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton(_Name, replacement);

        services.AddDashboardAuthentication(_Name, cfg => cfg.Mode = AuthMode.None);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredKeyedService<IAuthService>(_Name).Should().BeSameAs(replacement);
    }

    [Fact]
    public void add_dashboard_authentication_binds_the_named_config_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Mode"] = "ApiKey",
                    ["ApiKey"] = "secret",
                    ["SessionTimeoutMinutes"] = "15",
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDashboardAuthentication(_Name, configuration);

        using var provider = services.BuildServiceProvider();
        var config = provider.GetRequiredService<IOptionsMonitor<AuthConfig>>().Get(_Name);
        config.Mode.Should().Be(AuthMode.ApiKey);
        config.SessionTimeoutMinutes.Should().Be(15);
        provider.GetRequiredService<IOptionsMonitor<AuthConfig>>().Get(_OtherName).Mode.Should().Be(AuthMode.None);
    }

    [Fact]
    public void add_dashboard_authentication_configures_the_named_config_with_the_service_provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new ApiKeySource("from-services"));

        services.AddDashboardAuthentication(
            _Name,
            (cfg, sp) =>
            {
                cfg.Mode = AuthMode.ApiKey;
                cfg.ApiKey = sp.GetRequiredService<ApiKeySource>().Key;
            }
        );

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptionsMonitor<AuthConfig>>().Get(_Name).ApiKey.Should().Be("from-services");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void add_dashboard_authentication_rejects_a_blank_name(string name)
    {
        var services = new ServiceCollection();

        var act = () => services.AddDashboardAuthentication(name, _ => { });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void add_dashboard_authentication_throws_for_basic_mode_without_credentials()
    {
        var act = () => _ResolveConfig(cfg => cfg.Mode = AuthMode.Basic);

        act.Should().Throw<OptionsValidationException>().WithMessage("*BasicCredentials*");
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
        services.AddDashboardAuthentication(_Name, setupAction);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Resolving the keyed auth service reads the named AuthConfig, which runs the FluentValidation options
        // pipeline: the single validation point for every construction path.
        scope.ServiceProvider.GetRequiredKeyedService<IAuthService>(_Name);
    }

    private sealed record ApiKeySource(string Key);
}
