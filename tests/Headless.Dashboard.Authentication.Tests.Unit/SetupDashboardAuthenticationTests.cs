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
}
