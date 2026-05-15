// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.InMemoryQueue;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class InMemoryQueueSetupTests : TestBase
{
    [Fact]
    public async Task should_register_transport_services()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessMessaging(options =>
        {
            options.UseInMemoryMessageQueue();
        });

        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetService<ITransport>().Should().NotBeNull();
    }

    [Fact]
    public async Task should_register_memory_queue_as_singleton()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessMessaging(options =>
        {
            options.UseInMemoryMessageQueue();
        });

        await using var provider = services.BuildServiceProvider();

        // when
        var queue1 = provider.GetService<MemoryQueue>();
        var queue2 = provider.GetService<MemoryQueue>();

        // then
        queue1.Should().NotBeNull();
        queue1.Should().BeSameAs(queue2);
    }

    [Fact]
    public async Task should_register_consumer_client_factory()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessMessaging(options =>
        {
            options.UseInMemoryMessageQueue();
        });

        await using var provider = services.BuildServiceProvider();

        // when
        var factory = provider.GetService<IConsumerClientFactory>();

        // then
        factory.Should().NotBeNull();
        factory.Should().BeOfType<InMemoryConsumerClientFactory>();
    }

    [Fact]
    public async Task should_register_transport()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessMessaging(options =>
        {
            options.UseInMemoryMessageQueue();
        });

        await using var provider = services.BuildServiceProvider();

        // when
        var transport = provider.GetService<ITransport>();

        // then
        transport.Should().NotBeNull();
        transport.Should().BeOfType<InMemoryQueueTransport>();
    }

    [Fact]
    public async Task should_register_message_queue_marker_service()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessMessaging(options =>
        {
            options.UseInMemoryMessageQueue();
        });

        await using var provider = services.BuildServiceProvider();

        // when
        var marker = provider.GetService<MessageQueueMarkerService>();

        // then
        marker.Should().NotBeNull();
        marker!.Name.Should().Be("InMemoryQueue");
    }

    [Fact]
    public void should_return_setup_for_method_chaining()
    {
        // given
        var setup = new MessagingSetupBuilder(new ServiceCollection(), new MessagingOptions(), new ConsumerRegistry());

        // when
        var result = setup.UseInMemoryMessageQueue();

        // then
        result.Should().BeSameAs(setup);
    }
}
