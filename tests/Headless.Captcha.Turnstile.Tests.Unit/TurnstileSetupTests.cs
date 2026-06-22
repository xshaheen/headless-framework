// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Captcha;
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
        services.AddHeadlessCaptcha(builder =>
            builder.UseTurnstile(options =>
            {
                options.SiteKey = "k";
                options.SiteSecret = "s";
            })
        );

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
            builder
                .UseTurnstile(options =>
                {
                    options.SiteKey = "tk";
                    options.SiteSecret = "ts";
                })
                .UseReCaptchaV3(
                    "recaptcha",
                    options =>
                    {
                        options.SiteKey = "rk";
                        options.SiteSecret = "rs";
                    }
                )
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
            builder
                .UseTurnstile(options =>
                {
                    options.SiteKey = "tk";
                    options.SiteSecret = "ts";
                })
                .UseReCaptchaV3(
                    "recaptcha",
                    options =>
                    {
                        options.SiteKey = "rk";
                        options.SiteSecret = "rs";
                    }
                )
        );

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ICaptchaProvider>();

        provider.RegisteredNames.Should().BeEquivalentTo([CaptchaConstants.TurnstileProvider, "recaptcha"]);
    }

    [Fact]
    public void missing_site_secret_fails_options_validation()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder =>
            builder.UseTurnstile(options =>
            {
                options.SiteKey = "k";
                options.SiteSecret = "";
            })
        );

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
        services.AddHeadlessCaptcha(builder =>
            builder.UseTurnstile(options =>
            {
                options.SiteKey = "k";
                options.SiteSecret = "s";
            })
        );

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ICaptchaProvider>();

        var act = () => provider.GetVerifier("nonexistent");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*nonexistent*")
            .WithMessage($"*{CaptchaConstants.TurnstileProvider}*");
    }
}
