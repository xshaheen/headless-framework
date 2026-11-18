// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.RabbitMq;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// A named RabbitMQ transport builds everything from options of its own, so it never shares a host name, a connection
/// pool, or a setting with the default RabbitMQ transport.
/// </summary>
public sealed class NamedRabbitMqSetupTests : TestBase
{
    private const string _Partner = "partner";
    private const string _PartnerOptions = "Headless.Messaging.Transport.partner";

    public static TheoryData<string> Overloads => ["host-name", "configuration", "service-provider"];

    [Theory]
    [MemberData(nameof(Overloads))]
    public async Task should_register_a_named_rabbitmq_transport_from_its_own_options(string overload)
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        // Credentials for every RabbitMQ options instance, the default and the named one, which validate on resolve.
        services.ConfigureAll<RabbitMqMessagingOptions>(options =>
        {
            options.UserName = "headless-tests";
            options.Password = "not-a-real-secret";
        });
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseRabbitMq("rabbit-internal");
            var partner = setup.AddTransport(_Partner);
            _ = overload switch
            {
                "host-name" => partner.UseRabbitMq("rabbit-partner"),
                "configuration" => partner.UseRabbitMq(
                    new ConfigurationBuilder()
                        .AddInMemoryCollection([new KeyValuePair<string, string?>("HostName", "rabbit-partner")])
                        .Build()
                ),
                _ => partner.UseRabbitMq((options, _) => options.HostName = "rabbit-partner"),
            };
        });
        await using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptionsMonitor<RabbitMqMessagingOptions>>();
        var transport = provider.GetRequiredService<MessagingTransports>().Get(_Partner);

        // then
        options.Get(_PartnerOptions).HostName.Should().Be("rabbit-partner");
        options.CurrentValue.HostName.Should().Be("rabbit-internal", "the default transport keeps its own options");
        transport.Bus.Should().NotBeNull().And.NotBeSameAs(provider.GetRequiredService<IBusTransport>());
        transport.Queue.Should().NotBeNull().And.NotBeSameAs(provider.GetRequiredService<IQueueTransport>());
        transport.ConsumerClientFactory.Should().NotBeSameAs(provider.GetRequiredService<IConsumerClientFactory>());
        transport.Provisioners.Should().ContainSingle();
        transport.Capabilities.Providers.Should().Contain(p => p.Role == MessagingProviderRole.Transport);
    }
}
