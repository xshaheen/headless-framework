// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using INatsConnection = NATS.Client.Core.INatsConnection;
using NatsOpts = NATS.Client.Core.NatsOpts;

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
        var act = () => setup.UseNats((Action<NatsMessagingOptions>)null!);

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
    public async Task should_register_nats_transport_services_through_add_headless_messaging()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessMessaging(setup => setup.UseNats("nats://localhost:4222"));

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBusTransport>().Should().BeOfType<NatsTransport>();
        provider.GetRequiredService<IQueueTransport>().Should().BeOfType<NatsTransport>();
        provider.GetRequiredService<IConsumerClientFactory>().Should().BeOfType<NatsConsumerClientFactory>();
        provider.GetRequiredService<INatsConnectionPool>().Should().BeOfType<NatsConnectionPool>();
        provider
            .GetRequiredService<IOptions<NatsMessagingOptions>>()
            .Value.Servers.Should()
            .Be("nats://localhost:4222");
    }

    [Fact]
    public async Task should_publish_over_the_application_connection_when_use_connection()
    {
        // given - an app that registers its own NATS connection and hands it to Headless
        var appConnection = Substitute.For<INatsConnection>();
        appConnection.Opts.Returns(NatsOpts.Default with { Url = "nats://app-host:4222" });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(appConnection);
        services.AddHeadlessMessaging(setup =>
            setup.UseNats(options => options.UseConnection(sp => sp.GetRequiredService<INatsConnection>()))
        );

        // when
        await using var provider = services.BuildServiceProvider();
        var pool = provider.GetRequiredService<INatsConnectionPool>();

        // then
        pool.GetConnection().Should().BeSameAs(appConnection);
        pool.ConnectionOpts.Url.Should().Be("nats://app-host:4222");
    }

    [Fact]
    public async Task should_declare_every_instance_support_on_the_transport_capability()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup => setup.UseNats("nats://localhost:4222"));
        await using var provider = services.BuildServiceProvider();

        provider
            .GetServices<MessagingProviderCapabilities>()
            .Single(x => x.Role == MessagingProviderRole.Transport)
            .SupportsEveryInstance.Should()
            .BeTrue();
    }

    private static MessagingSetupBuilder _CreateSetup()
    {
        return new MessagingSetupBuilder(new ServiceCollection(), new MessagingOptions());
    }
}
