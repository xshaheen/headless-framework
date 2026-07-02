// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;
using Headless.Sms.Cequens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class CequensNamedSetupTests
{
    private static IConfiguration _Config(string senderName)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["SingleSmsEndpoint"] = "https://example.test/sms",
                    ["TokenEndpoint"] = "https://example.test/auth",
                    ["ApiKey"] = "api-key",
                    ["UserName"] = "user",
                    ["SenderName"] = senderName,
                }
            )
            .Build();
    }

    private static ServiceCollection _Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);

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
            setup.UseCequens(_Config("DEFAULT-SENDER"));
            setup.AddNamed("otp", instance => instance.UseCequens(_Config("OTP-SENDER")));
            setup.AddNamed("marketing", instance => instance.UseCequens(_Config("MKT-SENDER")));
        });
        using var provider = services.BuildServiceProvider();

        // then - each instance binds its own named options (proves Get(name), not CurrentValue).
        var monitor = provider.GetRequiredService<IOptionsMonitor<CequensSmsOptions>>();
        monitor.Get("otp").SenderName.Should().Be("OTP-SENDER");
        monitor.Get("marketing").SenderName.Should().Be("MKT-SENDER");

        // each instance owns a distinct sender (no first-wins TryAdd collision).
        var senderProvider = provider.GetRequiredService<ISmsSenderProvider>();
        senderProvider.GetSender("otp").Should().NotBeSameAs(senderProvider.GetSender("marketing"));

        // each instance owns its own named HttpClient with its own resilience pipeline; an unregistered
        // client name has no handler actions.
        var httpOptions = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>();
        httpOptions.Get("Headless:CequensSms:otp").HttpMessageHandlerBuilderActions.Should().NotBeEmpty();
        httpOptions.Get("Headless:CequensSms:marketing").HttpMessageHandlerBuilderActions.Should().NotBeEmpty();
        httpOptions.Get("Headless:CequensSms:unregistered").HttpMessageHandlerBuilderActions.Should().BeEmpty();
    }

    [Fact]
    public void named_instance_should_not_bleed_into_default_and_vice_versa()
    {
        // given
        var services = _Services();

        // when - a default Cequens sender plus a named Cequens instance.
        services.AddHeadlessSms(setup =>
        {
            setup.UseCequens(_Config("DEFAULT-SENDER"));
            setup.AddNamed("otp", instance => instance.UseCequens(_Config("OTP-SENDER")));
        });
        using var provider = services.BuildServiceProvider();

        // then - the default (unkeyed) options and the named options stay on their side of the keyed boundary.
        var monitor = provider.GetRequiredService<IOptionsMonitor<CequensSmsOptions>>();
        monitor.CurrentValue.SenderName.Should().Be("DEFAULT-SENDER");
        monitor.Get("otp").SenderName.Should().Be("OTP-SENDER");

        provider.GetRequiredService<ISmsSender>().Should().BeOfType<CequensSmsSender>();
        var named = provider.GetRequiredKeyedService<ISmsSender>("otp");
        named.Should().BeOfType<CequensSmsSender>();
        named.Should().NotBeSameAs(provider.GetRequiredService<ISmsSender>());
    }

    [Fact]
    public void named_instance_should_forward_keyed_bulk_sender_to_same_instance()
    {
        // given
        var services = _Services();

        // when
        services.AddHeadlessSms(setup =>
        {
            setup.UseCequens(_Config("DEFAULT-SENDER"));
            setup.AddNamed("otp", instance => instance.UseCequens(_Config("OTP-SENDER")));
        });
        using var provider = services.BuildServiceProvider();

        // then - the capability probe works on the factory-returned sender and the keyed forward matches.
        var named = provider.GetRequiredService<ISmsSenderProvider>().GetSender("otp");
        named.Should().BeAssignableTo<IBulkSmsSender>();
        provider.GetRequiredKeyedService<IBulkSmsSender>("otp").Should().BeSameAs(named);
    }

    [Fact]
    public void container_should_dispose_each_named_sender_independently()
    {
        // given - two named Cequens senders, each an IDisposable keyed singleton owned by the container.
        var services = _Services();
        services.AddHeadlessSms(setup =>
        {
            setup.UseCequens(_Config("DEFAULT-SENDER"));
            setup.AddNamed("otp", instance => instance.UseCequens(_Config("OTP-SENDER")));
            setup.AddNamed("marketing", instance => instance.UseCequens(_Config("MKT-SENDER")));
        });
        var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredKeyedService<ISmsSender>("otp");
        var second = provider.GetRequiredKeyedService<ISmsSender>("marketing");
        first.Should().NotBeSameAs(second);

        // when / then - container disposal disposes both instances without double-dispose errors.
        var dispose = () => provider.Dispose();
        dispose.Should().NotThrow();
        dispose.Should().NotThrow();
    }
}
