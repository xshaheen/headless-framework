// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Captcha;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Tests;

/// <summary>
/// A reCAPTCHA token is single-use, so the verify POST must reach the wire once even when the host adds the standard
/// resilience handler to every client (as ServiceDefaults does): that handler would otherwise wrap the provider's
/// pipeline and replay the token.
/// </summary>
public sealed class ReCaptchaResilienceTests : TestBase
{
    [Theory]
    [InlineData(CaptchaConstants.ReCaptchaV2Provider)]
    [InlineData(CaptchaConstants.ReCaptchaV3Provider)]
    public async Task should_send_verify_post_once_when_a_host_wide_handler_is_configured(string providerName)
    {
        // given
        using var stub = new StubSiteVerifyHandler();
        stub.EnqueueJson(HttpStatusCode.ServiceUnavailable, "{}");

        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler(_MakeRetryFast));
        services.AddHeadlessCaptcha(builder =>
        {
            if (providerName is CaptchaConstants.ReCaptchaV2Provider)
            {
                builder.UseReCaptchaV2(_Configuration());
            }
            else
            {
                builder.UseReCaptchaV3(_Configuration());
            }
        });
        services.AddHttpClient(providerName).ConfigurePrimaryHttpMessageHandler(() => stub);

        await using var provider = services.BuildServiceProvider();
        var verifier = provider.GetRequiredService<ICaptchaVerifier>();

        // when
        var act = async () => await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "token" }, AbortToken);

        // then
        await act.Should().ThrowAsync<HttpRequestException>();
        stub.InvocationCount.Should().Be(1, "a re-sent single-use token fails as a duplicate");
    }

    // A fixed 5ms delay keeps a regression (a retried POST) fast to report instead of waiting out the stock backoff.
    private static void _MakeRetryFast(HttpStandardResilienceOptions options)
    {
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.BackoffType = DelayBackoffType.Constant;
        options.Retry.UseJitter = false;
        options.Retry.Delay = TimeSpan.FromMilliseconds(5);
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
}
