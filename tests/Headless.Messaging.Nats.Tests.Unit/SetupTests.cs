// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Fact]
    public void should_allow_method_chaining_with_bootstrap_servers()
    {
        // given
        var setup = _CreateSetup();

        // when
        var result = setup.UseNats("nats://custom-server:4222");

        // then
        result.Should().BeSameAs(setup);
    }

    [Fact]
    public void should_allow_method_chaining_with_configure_action()
    {
        // given
        var setup = _CreateSetup();

        // when
        var result = setup.UseNats(opt =>
        {
            opt.Servers = "nats://configured-server:4222";
            opt.ConnectionPoolSize = 20;
        });

        // then
        result.Should().BeSameAs(setup);
    }

    [Fact]
    public void should_throw_when_configure_action_is_null()
    {
        // given
        var setup = _CreateSetup();

        // when
        var act = () => setup.UseNats((Action<MessagingNatsOptions>)null!);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_allow_default_servers_when_bootstrap_servers_is_null()
    {
        // given
        var setup = _CreateSetup();

        // when
        var result = setup.UseNats(bootstrapServers: null);

        // then
        result.Should().BeSameAs(setup);
    }

    [Fact]
    public void should_allow_empty_configure_action()
    {
        // given
        var setup = _CreateSetup();

        // when
        var result = setup.UseNats(_ => { });

        // then
        result.Should().BeSameAs(setup);
    }

    [Fact]
    public async Task should_register_nats_transport_services_through_AddHeadlessMessaging()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessMessaging(setup => setup.UseNats("nats://localhost:4222"));

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ITransport>().Should().BeOfType<NatsTransport>();
        provider.GetRequiredService<IConsumerClientFactory>().Should().BeOfType<NatsConsumerClientFactory>();
        provider.GetRequiredService<INatsConnectionPool>().Should().BeOfType<NatsConnectionPool>();
        provider
            .GetRequiredService<IOptions<MessagingNatsOptions>>()
            .Value.Servers.Should()
            .Be("nats://localhost:4222");
    }

    private static MessagingSetupBuilder _CreateSetup()
    {
        return new MessagingSetupBuilder(new ServiceCollection(), new MessagingOptions(), new ConsumerRegistry());
    }
}
