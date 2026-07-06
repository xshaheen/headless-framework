// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Captcha;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>Builder/DI behavior for Turnstile: default resolution, keyed multi-provider selection, validation.</summary>
public sealed class TurnstileSetupTests
{
    [Fact]
    public void default_provider_resolves_unkeyed_and_by_canonical_key()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder => builder.UseTurnstile(_ => { }));

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredService<ICaptchaVerifier>().Should().BeAssignableTo<ITurnstileVerifier>();
        serviceProvider.GetRequiredService<ITurnstileVerifier>().Should().NotBeNull();

        var provider = serviceProvider.GetRequiredService<ICaptchaProvider>();
        provider.GetVerifier(CaptchaConstants.TurnstileProvider).Should().BeAssignableTo<ITurnstileVerifier>();
    }

    [Fact]
    public void registering_turnstile_and_recaptcha_resolves_each_by_name()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder =>
            builder.UseTurnstile(_ => { }).AddNamed("recaptcha", instance => instance.UseReCaptchaV3(_ => { }))
        );

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ICaptchaProvider>();

        provider.GetVerifier("recaptcha").Should().BeAssignableTo<IReCaptchaV3Verifier>();
        provider.GetVerifier(CaptchaConstants.TurnstileProvider).Should().BeAssignableTo<ITurnstileVerifier>();
        serviceProvider.GetRequiredService<ICaptchaVerifier>().Should().BeAssignableTo<ITurnstileVerifier>();
    }

    [Fact]
    public void registered_names_includes_default_key_and_named_instances()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder =>
            builder.UseTurnstile(_ => { }).AddNamed("recaptcha", instance => instance.UseReCaptchaV3(_ => { }))
        );

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ICaptchaProvider>();

        provider.RegisteredNames.Should().BeEquivalentTo([CaptchaConstants.TurnstileProvider, "recaptcha"]);
    }

    [Fact]
    public void missing_site_secret_fails_options_validation()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder => builder.UseTurnstile(_Section("Turnstile", key: "k", secret: "")));

        using var serviceProvider = services.BuildServiceProvider();

        var act = () =>
            serviceProvider
                .GetRequiredService<IOptionsMonitor<TurnstileOptions>>()
                .Get(CaptchaConstants.TurnstileProvider);

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void resolving_unregistered_name_throws_actionable_error()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder => builder.UseTurnstile(_ => { }));

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ICaptchaProvider>();

        var act = () => provider.GetVerifier("nonexistent");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*nonexistent*")
            .WithMessage($"*{CaptchaConstants.TurnstileProvider}*");
    }

    [Fact]
    public void default_configuration_overload_binds_the_section()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Headless:Captcha:Turnstile:SiteKey"] = "cfg-key",
                    ["Headless:Captcha:Turnstile:SiteSecret"] = "cfg-secret",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder =>
            builder.UseTurnstile(configuration.GetSection("Headless:Captcha:Turnstile"))
        );

        using var serviceProvider = services.BuildServiceProvider();

        var options = serviceProvider
            .GetRequiredService<IOptionsMonitor<TurnstileOptions>>()
            .Get(CaptchaConstants.TurnstileProvider);

        options.SiteKey.Should().Be("cfg-key");
        options.SiteSecret.Should().Be("cfg-secret");
        serviceProvider.GetRequiredService<ITurnstileVerifier>().Should().NotBeNull();
    }

    [Fact]
    public void default_service_provider_overload_resolves_verifier()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder => builder.UseTurnstile((_, _) => { }));

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredService<ITurnstileVerifier>().Should().NotBeNull();
    }

    [Fact]
    public void named_configuration_overload_resolves_by_name()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder =>
            builder.AddNamed("ts-cfg", instance => instance.UseTurnstile(_Section("ts", "ck", "cs")))
        );

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider
            .GetRequiredService<ICaptchaProvider>()
            .GetVerifier("ts-cfg")
            .Should()
            .BeAssignableTo<ITurnstileVerifier>();
    }

    [Fact]
    public void named_service_provider_overload_resolves_by_name()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder =>
            builder.AddNamed("ts-sp", instance => instance.UseTurnstile((_, _) => { }))
        );

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider
            .GetRequiredService<ICaptchaProvider>()
            .GetVerifier("ts-sp")
            .Should()
            .BeAssignableTo<ITurnstileVerifier>();
    }

    private static IConfigurationSection _Section(string section, string key, string secret) =>
        new ConfigurationBuilder()
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
