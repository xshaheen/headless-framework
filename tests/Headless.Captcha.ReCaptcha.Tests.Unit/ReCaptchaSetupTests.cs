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
        services.AddHeadlessCaptcha(builder =>
            builder.UseReCaptchaV3(options =>
            {
                options.SiteKey = "k";
                options.SiteSecret = "s";
            })
        );

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredService<ICaptchaVerifier>().Should().BeAssignableTo<IReCaptchaV3Verifier>();
        serviceProvider.GetRequiredService<IReCaptchaV3Verifier>().Should().NotBeNull();
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
    public void v2_default_resolves_as_plain_captcha_verifier()
    {
        var services = new ServiceCollection();
        services.AddHeadlessCaptcha(builder =>
            builder.UseReCaptchaV2(options =>
            {
                options.SiteKey = "k";
                options.SiteSecret = "s";
            })
        );

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredService<ICaptchaVerifier>().Should().NotBeNull();
        serviceProvider.GetService<IReCaptchaV3Verifier>().Should().BeNull();
    }
}
