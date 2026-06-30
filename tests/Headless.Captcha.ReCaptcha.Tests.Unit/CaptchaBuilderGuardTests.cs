// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Captcha;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>Guard / sentinel paths on <see cref="HeadlessCaptchaSetupBuilder"/> and <c>AddHeadlessCaptcha</c>.</summary>
public sealed class CaptchaBuilderGuardTests
{
    private static readonly Action<ReCaptchaOptions> _ValidOptions = options =>
    {
        options.SiteKey = "k";
        options.SiteSecret = "s";
    };

    [Fact]
    public void two_defaults_throws_invalid_operation()
    {
        var services = new ServiceCollection();

        Action act = () =>
            services.AddHeadlessCaptcha(builder =>
            {
                builder.UseReCaptchaV3(_ValidOptions);
                builder.UseReCaptchaV3(_ValidOptions); // second default — same or different type
            });

        act.Should().Throw<InvalidOperationException>().WithMessage("*at most one default*");
    }

    [Fact]
    public void two_defaults_different_types_throws_invalid_operation()
    {
        var services = new ServiceCollection();

        Action act = () =>
            services.AddHeadlessCaptcha(builder =>
            {
                builder.UseReCaptchaV3(_ValidOptions);
                builder.UseReCaptchaV2(_ValidOptions); // v2 as second default
            });

        act.Should().Throw<InvalidOperationException>().WithMessage("*at most one default*");
    }

    [Fact]
    public void reserved_name_throws_argument_exception()
    {
        var services = new ServiceCollection();

        // Any name that starts with "Headless.Captcha:" is reserved.
        Action act = () =>
            services.AddHeadlessCaptcha(builder => builder.UseReCaptchaV3("Headless.Captcha:Whatever", _ValidOptions));

        act.Should().Throw<ArgumentException>().WithMessage("*reserved*");
    }

    [Fact]
    public void duplicate_named_provider_throws_invalid_operation()
    {
        var services = new ServiceCollection();

        Action act = () =>
            services.AddHeadlessCaptcha(builder =>
            {
                builder.UseReCaptchaV3("my-provider", _ValidOptions);
                builder.UseReCaptchaV3("my-provider", _ValidOptions); // same name again
            });

        act.Should().Throw<InvalidOperationException>().WithMessage("*already*");
    }

    [Fact]
    public void no_providers_registered_throws_invalid_operation()
    {
        var services = new ServiceCollection();

        Action act = () =>
            services.AddHeadlessCaptcha(_ =>
            { /* intentionally empty */
            });

        act.Should().Throw<InvalidOperationException>().WithMessage("*at least one provider*");
    }

    [Fact]
    public void repeated_add_headless_captcha_throws_invalid_operation()
    {
        var services = new ServiceCollection();

        // First call succeeds.
        services.AddHeadlessCaptcha(builder => builder.UseReCaptchaV3(_ValidOptions));

        // Second call on the same collection must be rejected by the sentinel.
        Action act = () => services.AddHeadlessCaptcha(builder => builder.UseReCaptchaV3(_ValidOptions));

        act.Should().Throw<InvalidOperationException>().WithMessage("*AddHeadlessCaptcha was already called*");
    }
}
