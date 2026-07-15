// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;
using Headless.Sms.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Twilio.Clients;

namespace Tests;

public sealed class TwilioNamedSetupTests
{
    private static IConfiguration _Config(string sid)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Sid"] = sid,
                    ["AuthToken"] = "token",
                    ["PhoneNumber"] = "+15551234567",
                }
            )
            .Build();
    }

    private static ServiceCollection _Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        return services;
    }

    [Fact]
    public void should_build_distinct_keyed_twilio_clients_from_their_own_named_options()
    {
        // given - distinct account SIDs per instance so the test proves each keyed client is built from its
        // own options, not merely that the two client instances differ.
        var services = _Services();

        // when
        services.AddHeadlessSms(setup =>
        {
            setup.UseTwilio(_Config("AC0000000000000000000000000000001"));
            setup.AddNamed("otp", instance => instance.UseTwilio(_Config("AC0000000000000000000000000000002")));
            setup.AddNamed("marketing", instance => instance.UseTwilio(_Config("AC0000000000000000000000000000003")));
        });
        using var provider = services.BuildServiceProvider();

        // then - each keyed client carries its own credentials; the default client keeps its own.
        var otpClient = provider.GetRequiredKeyedService<ITwilioRestClient>("otp");
        var marketingClient = provider.GetRequiredKeyedService<ITwilioRestClient>("marketing");
        otpClient.Should().NotBeSameAs(marketingClient);
        otpClient.AccountSid.Should().Be("AC0000000000000000000000000000002");
        marketingClient.AccountSid.Should().Be("AC0000000000000000000000000000003");
        provider.GetRequiredService<ITwilioRestClient>().AccountSid.Should().Be("AC0000000000000000000000000000001");

        // each instance owns its own named HttpClient with its own resilience pipeline.
        var httpOptions = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>();
        httpOptions.Get("Headless:TwilioSms:otp").HttpMessageHandlerBuilderActions.Should().NotBeEmpty();
        httpOptions.Get("Headless:TwilioSms:marketing").HttpMessageHandlerBuilderActions.Should().NotBeEmpty();
        httpOptions.Get("Headless:TwilioSms:unregistered").HttpMessageHandlerBuilderActions.Should().BeEmpty();
    }

    [Fact]
    public void should_resolve_via_factory_and_keyed_di_with_default_unaffected_when_named_twilio()
    {
        // given
        var services = _Services();

        // when
        services.AddHeadlessSms(setup =>
        {
            setup.UseTwilio(_Config("AC0000000000000000000000000000001"));
            setup.AddNamed("otp", instance => instance.UseTwilio(_Config("AC0000000000000000000000000000002")));
        });
        using var provider = services.BuildServiceProvider();

        // then
        var monitor = provider.GetRequiredService<IOptionsMonitor<TwilioSmsOptions>>();
        monitor.CurrentValue.Sid.Should().Be("AC0000000000000000000000000000001");
        monitor.Get("otp").Sid.Should().Be("AC0000000000000000000000000000002");

        var named = provider.GetRequiredService<ISmsSenderProvider>().GetSender("otp");
        named.Should().BeOfType<TwilioSmsSender>();
        provider.GetRequiredKeyedService<ISmsSender>("otp").Should().BeSameAs(named);
        provider.GetRequiredService<ISmsSender>().Should().NotBeSameAs(named);
    }

    [Fact]
    public void should_not_expose_bulk_capability_when_named_twilio_sender()
    {
        // given - Twilio sends one recipient per API call; the capability probe must be false.
        var services = _Services();

        // when
        services.AddHeadlessSms(setup =>
        {
            setup.UseTwilio(_Config("AC0000000000000000000000000000001"));
            setup.AddNamed("otp", instance => instance.UseTwilio(_Config("AC0000000000000000000000000000002")));
        });
        using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetRequiredService<ISmsSenderProvider>()
            .GetSender("otp")
            .Should()
            .NotBeAssignableTo<IBulkSmsSender>();
        provider.GetKeyedService<IBulkSmsSender>("otp").Should().BeNull();
    }
}
