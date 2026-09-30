// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Api.ServiceDefaults;
using Headless.Http.Effects;
using Headless.Sms;
using Headless.Sms.Connekio;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Tests;

/// <summary>
/// Answers the ServiceDefaults stacking question with an attempt count: when an app applies
/// <c>AddHeadless()</c> (whose HttpClient defaults add the standard resilience handler to every
/// client) <i>and</i> registers an SMS provider whose own handler disables retry, the SMS
/// no-retry opt-out must still hold — the declared effect wins over the host-wide default.
/// </summary>
/// <remarks>
/// The provider's endpoints are validated as https URLs, so the wire is stubbed at the handler
/// level: the named client's primary handler counts attempts and answers 503. Attempt count is
/// exactly what the stacking question turns on, and it is visible there as on a real wire.
/// </remarks>
public sealed class ServiceDefaultsStackingTests : TestBase
{
    [Fact]
    public async Task should_not_retry_sms_send_when_service_defaults_handler_stacks()
    {
        // given - a host with ServiceDefaults HttpClient defaults ON (the default), plus Connekio
        // selected as the SMS provider.
        var counter = new AttemptCountingHandler();

        var builder = WebApplication.CreateBuilder();
        _AddDefaultHeadlessSecurityConfiguration(builder);

        builder.AddHeadless();
        builder.Services.AddHeadlessSms(setup =>
            setup.UseConnekio(
                options =>
                {
                    options.Sender = "SENDER";
                    options.AccountId = "account";
                    options.UserName = "user";
                    options.Password = "pass";
                },
                configureClient: null,
                configureResilience: null
            )
        );

        // Route the Connekio client's primary handler at the counter AFTER AddHeadless applied the
        // host-wide defaults, so both pipelines are in play exactly as in a real host.
        builder.Services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => counter));

        await using var provider = builder.Services.BuildServiceProvider();

        // when
        var sender = provider.GetRequiredService<ISmsSender>();
        var result = await sender.SendAsync(SmsRequests.Single(), AbortToken);

        // then - failed (503), and exactly one send reached the wire: neither the provider's own
        // pipeline nor the host-wide default pipeline retried it.
        result.Success.Should().BeFalse();
        counter.Attempts.Should().Be(1, "the SMS no-retry opt-out must survive the ServiceDefaults default handler");
    }

    private static void _AddDefaultHeadlessSecurityConfiguration(WebApplicationBuilder builder)
    {
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Headless:StringEncryption:DefaultPassPhrase"] = "TestPassPhrase123456",
                ["Headless:StringEncryption:InitVectorBytes"] = "VGVzdElWMDEyMzQ1Njc4OQ==",
                ["Headless:StringEncryption:DefaultSalt"] = "VGVzdFNhbHQ=",
                ["Headless:LookupHasher:DefaultSalt"] = "TestSalt",
            }
        );
    }

    private sealed class AttemptCountingHandler : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Attempts++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("t") }
            );
        }
    }
}
