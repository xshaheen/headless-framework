// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Captcha;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Conformance fixture for the Turnstile provider. Wires <c>UseTurnstile</c> through the real builder and overrides
/// the named HTTP client's primary handler with the test stub, so the conformance suite exercises the real DI and
/// verify path. Supplies Cloudflare-shaped siteverify bodies.
/// </summary>
public sealed class TurnstileVerifierFixture : ICaptchaVerifierFixture, IDisposable
{
    private readonly List<ServiceProvider> _providers = [];

    public string SuccessResponseBody =>
        """
            {"success":true,"challenge_ts":"2026-06-21T10:00:00Z","hostname":"example.com","action":"login","cdata":"session-123","error-codes":[]}
            """;

    public string RejectedResponseBody =>
        """
            {"success":false,"error-codes":["invalid-input-response"]}
            """;

    public ICaptchaVerifier CreateVerifier(StubSiteVerifyHandler handler)
    {
        return _Build(handler).GetRequiredService<ICaptchaVerifier>();
    }

    public ITurnstileVerifier CreateTurnstileVerifier(StubSiteVerifyHandler handler)
    {
        return _Build(handler).GetRequiredService<ITurnstileVerifier>();
    }

    private ServiceProvider _Build(StubSiteVerifyHandler handler)
    {
        var services = new ServiceCollection();

        services.AddHeadlessCaptcha(builder => builder.UseTurnstile(_Configuration()));

        services.AddHttpClient(CaptchaConstants.TurnstileProvider).ConfigurePrimaryHttpMessageHandler(() => handler);

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
