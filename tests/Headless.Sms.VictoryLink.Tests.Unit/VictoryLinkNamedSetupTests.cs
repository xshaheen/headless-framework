// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;
using Headless.Sms.VictoryLink;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class VictoryLinkNamedSetupTests
{
    private static IConfiguration _Config(string sender)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Endpoint"] = "https://example.test/sms",
                    ["Sender"] = sender,
                    ["UserName"] = "user",
                    ["Password"] = "password",
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
    public void should_isolate_options_and_http_clients_across_named_instances()
    {
        // given
        var services = _Services();

        // when - the same provider twice under different names.
        services.AddHeadlessSms(setup =>
        {
            setup.UseVictoryLink(_Config("DEFAULT-SENDER"));
            setup.AddNamed("otp", instance => instance.UseVictoryLink(_Config("OTP-SENDER")));
            setup.AddNamed("marketing", instance => instance.UseVictoryLink(_Config("MKT-SENDER")));
        });
        using var provider = services.BuildServiceProvider();

        // then - each instance binds its own named options (proves Get(name), not CurrentValue).
        var monitor = provider.GetRequiredService<IOptionsMonitor<VictoryLinkSmsOptions>>();
        monitor.Get("otp").Sender.Should().Be("OTP-SENDER");
        monitor.Get("marketing").Sender.Should().Be("MKT-SENDER");

        // each instance owns a distinct sender (no first-wins TryAdd collision).
        var senderProvider = provider.GetRequiredService<ISmsSenderProvider>();
        senderProvider.GetSender("otp").Should().NotBeSameAs(senderProvider.GetSender("marketing"));

        // each instance owns its own named HttpClient with its own resilience pipeline; an unregistered
        // client name has no handler actions.
        var httpOptions = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>();
        httpOptions.Get("Headless:VictoryLinkSms:otp").HttpMessageHandlerBuilderActions.Should().NotBeEmpty();
        httpOptions.Get("Headless:VictoryLinkSms:marketing").HttpMessageHandlerBuilderActions.Should().NotBeEmpty();
        httpOptions.Get("Headless:VictoryLinkSms:unregistered").HttpMessageHandlerBuilderActions.Should().BeEmpty();
    }

    [Fact]
    public void should_not_bleed_into_default_and_vice_versa_when_named_instance()
    {
        // given
        var services = _Services();

        // when - a default VictoryLink sender plus a named VictoryLink instance.
        services.AddHeadlessSms(setup =>
        {
            setup.UseVictoryLink(_Config("DEFAULT-SENDER"));
            setup.AddNamed("otp", instance => instance.UseVictoryLink(_Config("OTP-SENDER")));
        });
        using var provider = services.BuildServiceProvider();

        // then - the default (unkeyed) options and the named options stay on their side of the keyed boundary.
        var monitor = provider.GetRequiredService<IOptionsMonitor<VictoryLinkSmsOptions>>();
        monitor.CurrentValue.Sender.Should().Be("DEFAULT-SENDER");
        monitor.Get("otp").Sender.Should().Be("OTP-SENDER");

        provider.GetRequiredService<ISmsSender>().Should().BeOfType<VictoryLinkSmsSender>();
        var named = provider.GetRequiredKeyedService<ISmsSender>("otp");
        named.Should().BeOfType<VictoryLinkSmsSender>();
        named.Should().NotBeSameAs(provider.GetRequiredService<ISmsSender>());
    }

    [Fact]
    public void should_forward_keyed_bulk_sender_to_same_instance_when_named_instance()
    {
        // given
        var services = _Services();

        // when
        services.AddHeadlessSms(setup =>
        {
            setup.UseVictoryLink(_Config("DEFAULT-SENDER"));
            setup.AddNamed("otp", instance => instance.UseVictoryLink(_Config("OTP-SENDER")));
        });
        using var provider = services.BuildServiceProvider();

        // then - the capability probe works on the factory-returned sender and the keyed forward matches.
        var named = provider.GetRequiredService<ISmsSenderProvider>().GetSender("otp");
        named.Should().BeAssignableTo<IBulkSmsSender>();
        provider.GetRequiredKeyedService<IBulkSmsSender>("otp").Should().BeSameAs(named);
    }
}
