// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Captcha;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>Builder/DI behavior for reCAPTCHA: default resolution and the IConfiguration overload binding.</summary>
public sealed class ReCaptchaSetupTests
{
    [Fact]
    public void v3_default_resolves_unkeyed_and_as_v3_verifier()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder => builder.UseReCaptchaV3(_ => { }));

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredService<ICaptchaVerifier>().Should().BeAssignableTo<IReCaptchaV3Verifier>();
        serviceProvider.GetRequiredService<IReCaptchaV3Verifier>().Should().NotBeNull();
    }

    [Fact]
    public void v3_action_overload_configures_options()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder =>
            builder.UseReCaptchaV3(options =>
            {
                options.SiteKey = "act-key";
                options.SiteSecret = "act-secret";
            })
        );

        using var serviceProvider = services.BuildServiceProvider();

        var options = serviceProvider
            .GetRequiredService<IOptionsMonitor<ReCaptchaOptions>>()
            .Get(CaptchaConstants.ReCaptchaV3Provider);

        options.SiteKey.Should().Be("act-key");
        options.SiteSecret.Should().Be("act-secret");
    }

    [Fact]
    public void v3_configuration_overload_binds_the_section()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Headless:Captcha:ReCaptchaV3:SiteKey"] = "cfg-key",
                    ["Headless:Captcha:ReCaptchaV3:SiteSecret"] = "cfg-secret",
                    ["Headless:Captcha:ReCaptchaV3:VerifyBaseUrl"] = "https://example.test/",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder =>
            builder.UseReCaptchaV3(configuration.GetSection("Headless:Captcha:ReCaptchaV3"))
        );

        using var serviceProvider = services.BuildServiceProvider();

        var options = serviceProvider
            .GetRequiredService<IOptionsMonitor<ReCaptchaOptions>>()
            .Get(CaptchaConstants.ReCaptchaV3Provider);

        options.SiteKey.Should().Be("cfg-key");
        options.SiteSecret.Should().Be("cfg-secret");
        options.VerifyBaseUrl.Should().Be("https://example.test/");
    }

    [Fact]
    public void missing_site_key_fails_options_validation()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder => builder.UseReCaptchaV3(_Section("ReCaptchaV3", key: "", secret: "s")));

        using var serviceProvider = services.BuildServiceProvider();

        var act = () =>
            serviceProvider
                .GetRequiredService<IOptionsMonitor<ReCaptchaOptions>>()
                .Get(CaptchaConstants.ReCaptchaV3Provider);

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void missing_site_secret_fails_options_validation()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder => builder.UseReCaptchaV3(_Section("ReCaptchaV3", key: "k", secret: "")));

        using var serviceProvider = services.BuildServiceProvider();

        var act = () =>
            serviceProvider
                .GetRequiredService<IOptionsMonitor<ReCaptchaOptions>>()
                .Get(CaptchaConstants.ReCaptchaV3Provider);

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void invalid_verify_base_url_fails_options_validation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["ReCaptchaV3:SiteKey"] = "k",
                    ["ReCaptchaV3:SiteSecret"] = "s",
                    ["ReCaptchaV3:VerifyBaseUrl"] = "not-a-url",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder => builder.UseReCaptchaV3(configuration.GetSection("ReCaptchaV3")));

        using var serviceProvider = services.BuildServiceProvider();

        var act = () =>
            serviceProvider
                .GetRequiredService<IOptionsMonitor<ReCaptchaOptions>>()
                .Get(CaptchaConstants.ReCaptchaV3Provider);

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void v2_default_resolves_as_plain_captcha_verifier()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder => builder.UseReCaptchaV2(_ => { }));

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredService<ICaptchaVerifier>().Should().NotBeNull();
        serviceProvider.GetService<IReCaptchaV3Verifier>().Should().BeNull();
    }

    [Fact]
    public void v3_service_provider_overload_resolves_verifier()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder => builder.UseReCaptchaV3((_, _) => { }));

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredService<IReCaptchaV3Verifier>().Should().NotBeNull();
    }

    [Fact]
    public void v3_named_configuration_overload_resolves_by_name()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder =>
            builder.AddNamed("v3-named", instance => instance.UseReCaptchaV3(_Section("ReCaptchaV3", "ck", "cs")))
        );

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider
            .GetRequiredService<ICaptchaProvider>()
            .GetVerifier("v3-named")
            .Should()
            .BeAssignableTo<IReCaptchaV3Verifier>();
    }

    [Fact]
    public void v3_named_service_provider_overload_resolves_by_name()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder =>
            builder.AddNamed("v3-sp", instance => instance.UseReCaptchaV3((_, _) => { }))
        );

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider
            .GetRequiredService<ICaptchaProvider>()
            .GetVerifier("v3-sp")
            .Should()
            .BeAssignableTo<IReCaptchaV3Verifier>();
    }

    [Fact]
    public void v2_configuration_overload_binds_the_section()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder =>
            builder.UseReCaptchaV2(_Section("ReCaptchaV2", "cfg-key", "cfg-secret"))
        );

        using var serviceProvider = services.BuildServiceProvider();

        var options = serviceProvider
            .GetRequiredService<IOptionsMonitor<ReCaptchaOptions>>()
            .Get(CaptchaConstants.ReCaptchaV2Provider);

        options.SiteKey.Should().Be("cfg-key");
        options.SiteSecret.Should().Be("cfg-secret");
        serviceProvider.GetRequiredService<ICaptchaVerifier>().Should().NotBeNull();
    }

    [Fact]
    public void v2_service_provider_overload_resolves_verifier()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder => builder.UseReCaptchaV2((_, _) => { }));

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredService<ICaptchaVerifier>().Should().NotBeNull();
    }

    [Fact]
    public void v2_named_action_overload_resolves_by_name()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder =>
            builder.AddNamed("v2-named", instance => instance.UseReCaptchaV2(_ => { }))
        );

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredService<ICaptchaProvider>().GetVerifier("v2-named").Should().NotBeNull();
    }

    [Fact]
    public void v2_named_service_provider_overload_resolves_by_name()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder =>
            builder.AddNamed("v2-sp", instance => instance.UseReCaptchaV2((_, _) => { }))
        );

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredService<ICaptchaProvider>().GetVerifier("v2-sp").Should().NotBeNull();
    }

    [Fact]
    public void v2_named_configuration_overload_resolves_by_name()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder =>
            builder.AddNamed("v2-cfg", instance => instance.UseReCaptchaV2(_Section("ReCaptchaV2", "ck", "cs")))
        );

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredService<ICaptchaProvider>().GetVerifier("v2-cfg").Should().NotBeNull();
    }

    private static IConfigurationSection _Section(string section, string key, string secret)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [$"{section}:SiteKey"] = key,
                    [$"{section}:SiteSecret"] = secret,
                }
            )
            .Build()
            .GetSection(section);
    }
}
