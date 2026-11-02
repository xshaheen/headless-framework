// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Cryptography;
using Headless.Api.ServiceDefaults;
using Headless.Captcha;
using Headless.Payments.Paymob.CashOut;
using Headless.PushNotifications;
using Headless.Sms;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Tests;

/// <summary>
/// Answers the ServiceDefaults stacking question with attempt counts. <c>AddHeadless()</c> adds the standard
/// resilience handler to every client through <c>ConfigureHttpClientDefaults</c>; that handler becomes an outer
/// pipeline around the provider's own, so a provider-level no-retry opt-out is defeated unless the provider's
/// pipeline removes it. The declared effect must win regardless of which is registered first.
/// </summary>
/// <remarks>
/// Provider endpoints are validated as https URLs, so the wire is stubbed at the primary handler: it counts attempts
/// and answers 503. The attempt count is exactly what the stacking question turns on.
/// </remarks>
public sealed class ServiceDefaultsStackingTests : TestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_not_retry_sms_send_under_service_defaults(bool serviceDefaultsFirst)
    {
        // given - ServiceDefaults HttpClient defaults ON (the default), plus Connekio as the SMS provider.
        using var counter = new AttemptCountingHandler();
        var builder = _CreateBuilder(counter);

        _Register(
            builder,
            serviceDefaultsFirst,
            services =>
                services.AddHeadlessSms(setup =>
                    setup.UseConnekio(options =>
                    {
                        options.Sender = "SENDER";
                        options.AccountId = "account";
                        options.UserName = "user";
                        options.Password = "pass";
                    })
                )
        );

        await using var provider = builder.Services.BuildServiceProvider();

        // when
        var result = await provider.GetRequiredService<ISmsSender>().SendAsync(SmsRequests.Single(), AbortToken);

        // then - one attempt: neither the provider pipeline nor the host-wide default pipeline retried the send.
        result.Success.Should().BeFalse();
        counter
            .Count(HttpMethod.Post)
            .Should()
            .Be(1, "the declared Unsafe effect must survive the ServiceDefaults handler");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_not_retry_paymob_disburse_under_service_defaults(bool serviceDefaultsFirst)
    {
        // given
        using var counter = new AttemptCountingHandler();
        var builder = _CreateBuilder(counter);
        _Register(builder, serviceDefaultsFirst, _AddPaymobCashOut);

        await using var provider = builder.Services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var broker = scope.ServiceProvider.GetRequiredService<IPaymobCashOutBroker>();

        // when
        var act = async () =>
            await broker.DisburseAsync(CashOutDisburseRequest.Vodafone(100m, "01012345678"), AbortToken);

        // then - the payout POST reached the wire exactly once.
        await act.Should().ThrowAsync<PaymobCashOutException>();
        counter.Count(HttpMethod.Post).Should().Be(1, "a payout POST must never be retried automatically");
    }

    [Fact]
    public async Task should_still_retry_paymob_budget_read_under_service_defaults()
    {
        // given - the Unsafe declaration covers mutating methods only; reads stay retryable. The retry delay is
        // a fixed 5ms because the assertion counts attempts, not delay pacing; the stock exponential backoff
        // would otherwise be waited out on every run. The provider's own pipeline owns the pacing here: it
        // removes the host-wide handler, so the tuning must reach it through AddPaymobCashOut.
        using var counter = new AttemptCountingHandler();
        var builder = _CreateBuilder(counter);
        _Register(builder, serviceDefaultsFirst: true, services => _AddPaymobCashOut(services, _MakeRetryFast));

        await using var provider = builder.Services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var broker = scope.ServiceProvider.GetRequiredService<IPaymobCashOutBroker>();

        // when
        var act = async () => await broker.GetBudgetAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<PaymobCashOutException>();
        counter.Count(HttpMethod.Get).Should().BeGreaterThan(1);
    }

    [Theory]
    [InlineData(CaptchaProvider.ReCaptchaV2, true)]
    [InlineData(CaptchaProvider.ReCaptchaV2, false)]
    [InlineData(CaptchaProvider.ReCaptchaV3, true)]
    [InlineData(CaptchaProvider.ReCaptchaV3, false)]
    [InlineData(CaptchaProvider.Turnstile, true)]
    [InlineData(CaptchaProvider.Turnstile, false)]
    public async Task should_not_retry_captcha_verify_under_service_defaults(
        CaptchaProvider captchaProvider,
        bool serviceDefaultsFirst
    )
    {
        // given - a captcha token is single-use: a replayed siteverify POST is rejected as a duplicate, so a retry
        // after a transient 503 fails the user's verification instead of rescuing it.
        using var counter = new AttemptCountingHandler();
        var builder = _CreateBuilder(counter);
        _Register(builder, serviceDefaultsFirst, services => _AddCaptcha(services, captchaProvider));

        await using var provider = builder.Services.BuildServiceProvider();
        var verifier = provider.GetRequiredService<ICaptchaVerifier>();

        // when
        var act = async () =>
            await verifier.VerifyAsync(new CaptchaVerifyRequest { Response = "single-use-token" }, AbortToken);

        // then
        await act.Should().ThrowAsync<HttpRequestException>();
        counter.Count(HttpMethod.Post).Should().Be(1, "a single-use captcha token must be posted exactly once");
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    public async Task should_not_retry_apns_throttle_or_server_failure_under_service_defaults(
        HttpStatusCode statusCode,
        bool serviceDefaultsFirst
    )
    {
        // given - APNs' 429 throttles one device token and Apple asks senders to wait about 15 minutes after a 5xx,
        // so neither may be retried in-process; and APNs does not deduplicate, so a resend can show a notification
        // twice.
        using var counter = new AttemptCountingHandler(_ => _ApnsFailure(statusCode));
        var builder = _CreateBuilder(counter);
        _Register(builder, serviceDefaultsFirst, services => _AddApns(services, counter, _MakeApnsRetryFast));

        await using var provider = builder.Services.BuildServiceProvider();

        // when
        var response = await provider
            .GetRequiredService<IPushNotificationService>()
            .SendToDeviceAsync("device-1", PushNotificationRequests.Valid(), AbortToken);

        // then
        response.IsSucceeded().Should().BeFalse();
        counter.Count(HttpMethod.Post).Should().Be(1, $"APNs {(int)statusCode} must reach the wire exactly once");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_still_retry_apns_pre_send_fault_under_service_defaults(bool serviceDefaultsFirst)
    {
        // given - a connection refused before the request was written proves APNs never saw it, so the provider's
        // own pipeline retries it (at most twice); the host-wide pipeline must not multiply those attempts. The
        // caller's resilience tuning (fast constant delay) runs last and must not drop the provider's predicate.
        using var counter = new AttemptCountingHandler(_ =>
            throw new HttpRequestException(HttpRequestError.ConnectionError, "connection refused")
        );
        var builder = _CreateBuilder(counter);
        _Register(builder, serviceDefaultsFirst, services => _AddApns(services, counter, _MakeApnsRetryFast));

        await using var provider = builder.Services.BuildServiceProvider();

        // when
        var response = await provider
            .GetRequiredService<IPushNotificationService>()
            .SendToDeviceAsync("device-1", PushNotificationRequests.Valid(), AbortToken);

        // then - the first attempt plus the provider's two retries, and nothing more.
        response.IsSucceeded().Should().BeFalse();
        counter.Count(HttpMethod.Post).Should().Be(3);
    }

    public enum CaptchaProvider
    {
        ReCaptchaV2,
        ReCaptchaV3,
        Turnstile,
    }

    private static void _AddCaptcha(IServiceCollection services, CaptchaProvider captchaProvider)
    {
        services.AddHeadlessCaptcha(setup =>
        {
            switch (captchaProvider)
            {
                case CaptchaProvider.ReCaptchaV2:
                    setup.UseReCaptchaV2(options =>
                    {
                        options.SiteKey = "site-key";
                        options.SiteSecret = "site-secret";
                    });
                    break;
                case CaptchaProvider.ReCaptchaV3:
                    setup.UseReCaptchaV3(options =>
                    {
                        options.SiteKey = "site-key";
                        options.SiteSecret = "site-secret";
                    });
                    break;
                case CaptchaProvider.Turnstile:
                    setup.UseTurnstile(options =>
                    {
                        options.SiteKey = "site-key";
                        options.SiteSecret = "site-secret";
                    });
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(captchaProvider), captchaProvider, message: null);
            }
        });
    }

    private static void _AddApns(
        IServiceCollection services,
        AttemptCountingHandler counter,
        Action<HttpStandardResilienceOptions> configureResilience
    )
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyPem = key.ExportPkcs8PrivateKeyPem();

        services.AddHeadlessPushNotifications(setup =>
            setup.UseApns(
                options =>
                {
                    options.KeyId = "ABC123DEFG";
                    options.TeamId = "TEAM123456";
                    options.PrivateKey = privateKeyPem;
                    options.BundleId = "com.example.app";
                },
                configureResilience: configureResilience
            )
        );

        // APNs builds its own primary handler on its named client, which overrides the host-wide stub; the last
        // primary-handler registration wins, so this puts the attempt counter back on the wire.
        services.AddHttpClient("Headless:Apns").ConfigurePrimaryHttpMessageHandler(() => counter);
    }

    private static HttpResponseMessage _ApnsFailure(HttpStatusCode statusCode)
    {
        var reason = statusCode is HttpStatusCode.TooManyRequests ? "TooManyRequests" : "InternalServerError";

        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent($$"""{"reason":"{{reason}}"}""", System.Text.Encoding.UTF8, "application/json"),
        };
    }

    private static void _AddPaymobCashOut(IServiceCollection services)
    {
        _AddPaymobCashOut(services, configureResilience: null);
    }

    private static void _AddPaymobCashOut(
        IServiceCollection services,
        Action<HttpStandardResilienceOptions>? configureResilience
    )
    {
        // The options are init-only, so configuration binding is the overload a consumer uses.
        services.AddPaymobCashOut(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["ApiBaseUrl"] = "https://accept.paymob.test/v1/",
                        ["UserName"] = "user",
                        ["Password"] = "pass",
                        ["ClientId"] = "client",
                        ["ClientSecret"] = "secret",
                    }
                )
                .Build(),
            configureResilience: configureResilience
        );

        // Registered after AddPaymobCashOut so the last registration wins and no real token call is made.
        var authenticator = Substitute.For<IPaymobCashOutAuthenticator>();
        authenticator.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("token");
        services.AddSingleton(authenticator);
    }

    // The assertions count attempts, not delay pacing, so a retrying pipeline waits a fixed 5ms between
    // attempts instead of the stock exponential backoff (1s base plus jitter).
    private static void _MakeRetryFast(HttpStandardResilienceOptions options)
    {
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.BackoffType = DelayBackoffType.Constant;
        options.Retry.UseJitter = false;
        options.Retry.Delay = TimeSpan.FromMilliseconds(5);
    }

    // Only the pacing changes: the attempt count and predicate stay APNs's own, so the count the test asserts is the
    // provider's, and the caller's delegate is shown to run after the provider's defaults without undoing them.
    private static void _MakeApnsRetryFast(HttpStandardResilienceOptions options)
    {
        options.Retry.BackoffType = DelayBackoffType.Constant;
        options.Retry.UseJitter = false;
        options.Retry.Delay = TimeSpan.FromMilliseconds(5);
    }

    private static WebApplicationBuilder _CreateBuilder(AttemptCountingHandler counter)
    {
        var builder = WebApplication.CreateBuilder();

        builder.Services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => counter));

        return builder;
    }

    // Registration order must not matter: HttpClientFactory runs default-client configuration before named-client
    // configuration whichever was registered first.
    private static void _Register(
        WebApplicationBuilder builder,
        bool serviceDefaultsFirst,
        Action<IServiceCollection> registerProvider
    )
    {
        if (serviceDefaultsFirst)
        {
            builder.AddHeadless();
            registerProvider(builder.Services);
        }
        else
        {
            registerProvider(builder.Services);
            builder.AddHeadless();
        }
    }

    // Answers 503 unless a test supplies its own response; a responder that throws simulates a transport fault.
    private sealed class AttemptCountingHandler(Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
        : HttpMessageHandler
    {
        private readonly Dictionary<HttpMethod, int> _attempts = [];

        public int Count(HttpMethod method)
        {
            lock (_attempts)
            {
                return _attempts.GetValueOrDefault(method);
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            lock (_attempts)
            {
                _attempts[request.Method] = _attempts.GetValueOrDefault(request.Method) + 1;
            }

            if (respond is not null)
            {
                try
                {
                    return Task.FromResult(respond(request));
                }
                catch (HttpRequestException e)
                {
                    return Task.FromException<HttpResponseMessage>(e);
                }
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("transient") }
            );
        }
    }
}
