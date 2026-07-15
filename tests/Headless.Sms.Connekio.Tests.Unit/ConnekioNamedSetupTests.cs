// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;
using Headless.Sms.Connekio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class ConnekioNamedSetupTests
{
    private static IConfiguration _Config(string sender)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["SingleSmsEndpoint"] = "https://example.test/sms/single",
                    ["BatchSmsEndpoint"] = "https://example.test/sms/batch",
                    ["Sender"] = sender,
                    ["AccountId"] = "account",
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
            setup.UseConnekio(_Config("DEFAULT-SENDER"));
            setup.AddNamed("otp", instance => instance.UseConnekio(_Config("OTP-SENDER")));
            setup.AddNamed("marketing", instance => instance.UseConnekio(_Config("MKT-SENDER")));
        });
        using var provider = services.BuildServiceProvider();

        // then - each instance binds its own named options (proves Get(name), not CurrentValue).
        var monitor = provider.GetRequiredService<IOptionsMonitor<ConnekioSmsOptions>>();
        monitor.Get("otp").Sender.Should().Be("OTP-SENDER");
        monitor.Get("marketing").Sender.Should().Be("MKT-SENDER");

        // each instance owns a distinct sender (no first-wins TryAdd collision).
        var senderProvider = provider.GetRequiredService<ISmsSenderProvider>();
        senderProvider.GetSender("otp").Should().NotBeSameAs(senderProvider.GetSender("marketing"));

        // each instance owns its own named HttpClient with its own resilience pipeline; an unregistered
        // client name has no handler actions.
        var httpOptions = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>();
        httpOptions.Get("Headless:ConnekioSms:otp").HttpMessageHandlerBuilderActions.Should().NotBeEmpty();
        httpOptions.Get("Headless:ConnekioSms:marketing").HttpMessageHandlerBuilderActions.Should().NotBeEmpty();
        httpOptions.Get("Headless:ConnekioSms:unregistered").HttpMessageHandlerBuilderActions.Should().BeEmpty();
    }

    [Fact]
    public void should_not_bleed_into_default_and_vice_versa_when_named_instance()
    {
        // given
        var services = _Services();

        // when - a default Connekio sender plus a named Connekio instance.
        services.AddHeadlessSms(setup =>
        {
            setup.UseConnekio(_Config("DEFAULT-SENDER"));
            setup.AddNamed("otp", instance => instance.UseConnekio(_Config("OTP-SENDER")));
        });
        using var provider = services.BuildServiceProvider();

        // then - the default (unkeyed) options and the named options stay on their side of the keyed boundary.
        var monitor = provider.GetRequiredService<IOptionsMonitor<ConnekioSmsOptions>>();
        monitor.CurrentValue.Sender.Should().Be("DEFAULT-SENDER");
        monitor.Get("otp").Sender.Should().Be("OTP-SENDER");

        provider.GetRequiredService<ISmsSender>().Should().BeOfType<ConnekioSmsSender>();
        var named = provider.GetRequiredKeyedService<ISmsSender>("otp");
        named.Should().BeOfType<ConnekioSmsSender>();
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
            setup.UseConnekio(_Config("DEFAULT-SENDER"));
            setup.AddNamed("otp", instance => instance.UseConnekio(_Config("OTP-SENDER")));
        });
        using var provider = services.BuildServiceProvider();

        // then - the capability probe works on the factory-returned sender and the keyed forward matches.
        var named = provider.GetRequiredService<ISmsSenderProvider>().GetSender("otp");
        named.Should().BeAssignableTo<IBulkSmsSender>();
        provider.GetRequiredKeyedService<IBulkSmsSender>("otp").Should().BeSameAs(named);
    }
}
