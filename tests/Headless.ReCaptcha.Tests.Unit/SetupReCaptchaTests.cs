// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http;
using Headless.ReCaptcha;
using Headless.ReCaptcha.Contracts;
using Headless.ReCaptcha.V2;
using Headless.ReCaptcha.V3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupReCaptchaTests
{
    private static ServiceProvider BuildValidated(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);

        // ValidateScopes=true mirrors the ASP.NET Core Development default and is the regression guard:
        // resolving the named HttpClient would throw here if the factory callback used scoped IOptionsSnapshot.
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public void should_resolve_v3_service_and_named_client_under_scope_validation()
    {
        using var provider = BuildValidated(s =>
            s.AddReCaptchaV3(o =>
            {
                o.SiteKey = "key";
                o.SiteSecret = "secret";
            })
        );

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IReCaptchaSiteVerifyV3>().Should().NotBeNull();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(SetupReCaptcha.V3Name);
        client.BaseAddress.Should().Be(new Uri("https://www.google.com/"));
    }

    [Fact]
    public void should_resolve_v2_and_v3_side_by_side_with_independent_options()
    {
        using var provider = BuildValidated(s =>
        {
            s.AddReCaptchaV3(o =>
            {
                o.SiteKey = "v3-key";
                o.SiteSecret = "v3-secret";
            });
            s.AddReCaptchaV2(o =>
            {
                o.SiteKey = "v2-key";
                o.SiteSecret = "v2-secret";
            });
        });

        provider.GetRequiredService<IReCaptchaSiteVerifyV2>().Should().NotBeNull();
        provider.GetRequiredService<IReCaptchaSiteVerifyV3>().Should().NotBeNull();

        var monitor = provider.GetRequiredService<IOptionsMonitor<ReCaptchaOptions>>();
        monitor.Get(SetupReCaptcha.V3Name).SiteKey.Should().Be("v3-key");
        monitor.Get(SetupReCaptcha.V2Name).SiteKey.Should().Be("v2-key");
    }

    [Fact]
    public void should_bind_options_from_configuration_section()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["ReCaptcha:V3:SiteKey"] = "cfg-key",
                    ["ReCaptcha:V3:SiteSecret"] = "cfg-secret",
                }
            )
            .Build();

        using var provider = BuildValidated(s => s.AddReCaptchaV3(config.GetSection("ReCaptcha:V3")));

        var options = provider.GetRequiredService<IOptionsMonitor<ReCaptchaOptions>>().Get(SetupReCaptcha.V3Name);
        options.SiteKey.Should().Be("cfg-key");
        options.SiteSecret.Should().Be("cfg-secret");
    }

    [Fact]
    public void should_run_options_validation_when_required_values_are_missing()
    {
        using var provider = BuildValidated(s =>
            s.AddReCaptchaV3(o =>
            {
                o.SiteKey = "";
                o.SiteSecret = "";
            })
        );

        var act = () => provider.GetRequiredService<IOptionsMonitor<ReCaptchaOptions>>().Get(SetupReCaptcha.V3Name);

        act.Should().Throw<OptionsValidationException>();
    }
}
