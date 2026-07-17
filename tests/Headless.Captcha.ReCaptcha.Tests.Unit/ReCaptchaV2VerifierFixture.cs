// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Captcha;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>Conformance fixture for the reCAPTCHA v2 provider over the test stub handler.</summary>
public sealed class ReCaptchaV2VerifierFixture : ICaptchaVerifierFixture, IDisposable
{
    private readonly List<ServiceProvider> _providers = [];

    public string SuccessResponseBody =>
        """
            {"success":true,"challenge_ts":"2026-06-21T10:00:00Z","hostname":"example.com"}
            """;

    public string RejectedResponseBody =>
        """
            {"success":false,"error-codes":["invalid-input-response"]}
            """;

    public ICaptchaVerifier CreateVerifier(StubSiteVerifyHandler handler)
    {
        var services = new ServiceCollection();

        services.AddHeadlessCaptcha(builder => builder.UseReCaptchaV2(_Configuration()));

        services.AddHttpClient(CaptchaConstants.ReCaptchaV2Provider).ConfigurePrimaryHttpMessageHandler(() => handler);

        var serviceProvider = services.BuildServiceProvider();
        _providers.Add(serviceProvider);

        return serviceProvider.GetRequiredService<ICaptchaVerifier>();
    }

    private static IConfiguration _Configuration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["SiteKey"] = "test-site-key",
                    ["SiteSecret"] = "test-secret",
                }
            )
            .Build();
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        _providers.Clear();
    }
}
