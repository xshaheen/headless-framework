// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.InMemoryStorage;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Fact]
    public void should_register_in_memory_storage_services()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<MessagingOptions>();

        // when
        services.AddMessages(options =>
        {
            options.UseInMemoryStorage();
        });

        var provider = services.BuildServiceProvider();

        // then
        provider.GetService<IDataStorage>().Should().NotBeNull();
        provider.GetService<IStorageInitializer>().Should().NotBeNull();
        provider.GetService<MessageStorageMarkerService>().Should().NotBeNull();
    }

    [Fact]
    public void should_register_marker_service_with_correct_name()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<MessagingOptions>();
        services.AddMessages(options =>
        {
            options.UseInMemoryStorage();
        });

        var provider = services.BuildServiceProvider();

        // when
        var marker = provider.GetRequiredService<MessageStorageMarkerService>();

        // then
        marker.Name.Should().Be("InMemory");
    }

    [Fact]
    public void should_return_options_for_fluent_chaining()
    {
        // given
        var options = new MessagingOptions();

        // when
        var result = options.UseInMemoryStorage();

        // then
        result.Should().BeSameAs(options);
    }

    [Fact]
    public void should_register_data_storage_as_singleton()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<MessagingOptions>();
        services.AddMessages(options =>
        {
            options.UseInMemoryStorage();
        });

        var provider = services.BuildServiceProvider();

        // when
        var storage1 = provider.GetRequiredService<IDataStorage>();
        var storage2 = provider.GetRequiredService<IDataStorage>();

        // then
        storage1.Should().BeSameAs(storage2);
    }

    [Fact]
    public void should_register_storage_initializer_as_singleton()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<MessagingOptions>();
        services.AddMessages(options =>
        {
            options.UseInMemoryStorage();
        });

        var provider = services.BuildServiceProvider();

        // when
        var initializer1 = provider.GetRequiredService<IStorageInitializer>();
        var initializer2 = provider.GetRequiredService<IStorageInitializer>();

        // then
        initializer1.Should().BeSameAs(initializer2);
    }

    [Fact]
    public void should_resolve_data_storage_as_in_memory_implementation()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<MessagingOptions>();
        services.AddMessages(options =>
        {
            options.UseInMemoryStorage();
        });

        var provider = services.BuildServiceProvider();

        // when
        var storage = provider.GetRequiredService<IDataStorage>();

        // then
        storage.Should().BeOfType<InMemoryDataStorage>();
    }
}
