// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Captcha;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>Conformance fixture for the reCAPTCHA v3 provider over the test stub handler.</summary>
public sealed class ReCaptchaV3VerifierFixture : ICaptchaVerifierFixture, IDisposable
{
    private readonly List<ServiceProvider> _providers = [];

    public string SuccessResponseBody =>
        """
            {"success":true,"challenge_ts":"2026-06-21T10:00:00Z","hostname":"example.com","score":0.9,"action":"login","error-codes":[]}
            """;

    public string RejectedResponseBody =>
        """
            {"success":false,"error-codes":["timeout-or-duplicate"]}
            """;

    public ICaptchaVerifier CreateVerifier(StubSiteVerifyHandler handler)
    {
        return _Build(handler).GetRequiredService<ICaptchaVerifier>();
    }

    public IReCaptchaV3Verifier CreateV3Verifier(StubSiteVerifyHandler handler)
    {
        return _Build(handler).GetRequiredService<IReCaptchaV3Verifier>();
    }

    private ServiceProvider _Build(StubSiteVerifyHandler handler)
    {
        var services = new ServiceCollection();

        services.AddHeadlessCaptcha(builder => builder.UseReCaptchaV3(_Configuration()));

        services.AddHttpClient(CaptchaConstants.ReCaptchaV3Provider).ConfigurePrimaryHttpMessageHandler(() => handler);

        var serviceProvider = services.BuildServiceProvider();
        _providers.Add(serviceProvider);

        return serviceProvider;
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
