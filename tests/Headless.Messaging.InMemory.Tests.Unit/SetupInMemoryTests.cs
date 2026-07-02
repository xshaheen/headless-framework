// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Xml.Linq;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.InMemory;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupInMemoryTests : TestBase
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
            options.UseInMemory();
        });

        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetService<ITransport>().Should().NotBeNull();
        provider.GetService<IBusTransport>().Should().NotBeNull();
        provider.GetService<IQueueTransport>().Should().NotBeNull();
    }

    [Fact]
    public async Task should_register_memory_queue_as_singleton()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessMessaging(options =>
        {
            options.UseInMemory();
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

        services.AddHeadlessMessaging(options => options.UseInMemory());

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
            options.UseInMemory();
        });

        await using var provider = services.BuildServiceProvider();

        // when
        var transport = provider.GetService<ITransport>();

        // then
        transport.Should().NotBeNull();
        transport.Should().BeOfType<InMemoryBusTransport>();
        provider.GetService<IBusTransport>().Should().BeOfType<InMemoryBusTransport>();
        provider.GetService<IQueueTransport>().Should().BeOfType<InMemoryQueueTransport>();
    }

    [Fact]
    public void should_reference_registered_capability_abstractions_directly()
    {
        // given
        var projectPath = Path.Combine(
            _FindRepositoryRoot(),
            "src",
            "Headless.Messaging.InMemory",
            "Headless.Messaging.InMemory.csproj"
        );

        // when
        var projectReferences = XDocument
            .Load(projectPath)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .ToList();

        // then
        projectReferences
            .Should()
            .Contain(@"..\Headless.Messaging.Bus.Abstractions\Headless.Messaging.Bus.Abstractions.csproj");
        projectReferences
            .Should()
            .Contain(@"..\Headless.Messaging.Queue.Abstractions\Headless.Messaging.Queue.Abstractions.csproj");
    }

    [Fact]
    public async Task should_register_message_queue_marker_service()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessMessaging(options =>
        {
            options.UseInMemory();
        });

        await using var provider = services.BuildServiceProvider();

        // when
        var marker = provider.GetService<MessageQueueMarkerService>();

        // then
        marker.Should().NotBeNull();
        marker!.Name.Should().Be("InMemory");
    }

    [Fact]
    public void should_return_setup_for_method_chaining()
    {
        // given
        var setup = new MessagingSetupBuilder(new ServiceCollection(), new MessagingOptions(), new ConsumerRegistry());

        // when
        var result = setup.UseInMemory();

        // then
        result.Should().BeSameAs(setup);
    }

    private static string _FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "headless-framework.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root from test output directory.");
    }
}
