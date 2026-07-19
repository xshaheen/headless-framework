// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.RabbitMq;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tests.Capabilities;

namespace Tests;

[Collection<RabbitMqFixture>]
public sealed class RabbitMqConsumerClientHarnessTests(RabbitMqFixture fixture) : ConsumerClientTestsBase
{
    private readonly IServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();
    private readonly List<ConnectionChannelPool> _connectionPools = [];

    protected override ConsumerClientCapabilities Capabilities =>
        new()
        {
            SupportsFetchTopics = true,
            SupportsConcurrentProcessing = true,
            SupportsReject = true,
            SupportsGracefulShutdown = true,
        };

    protected override Task<IConsumerClient> GetConsumerClientAsync()
    {
        var messagingOptions = Options.Create(new MessagingOptions { Version = "v1" });
        var rabbitOptions = Options.Create(
            new RabbitMqMessagingOptions
            {
                HostName = fixture.HostName,
                Port = fixture.Port,
                UserName = fixture.UserName,
                Password = fixture.Password,
                ExchangeName = $"consumer-tests-{Guid.NewGuid():N}",
            }
        );

        var pool = new ConnectionChannelPool(
            NullLogger<ConnectionChannelPool>.Instance,
            messagingOptions,
            rabbitOptions
        );
        _connectionPools.Add(pool);

        return Task.FromResult<IConsumerClient>(
            new RabbitMqConsumerClient($"test-group-{Guid.NewGuid():N}", 1, pool, rabbitOptions, _serviceProvider)
        );
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var pool in _connectionPools)
        {
            await pool.DisposeAsync();
        }

        _connectionPools.Clear();

        if (_serviceProvider is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }

        await base.DisposeAsyncCore();
    }

    [Fact]
    public override Task should_subscribe_to_topic()
    {
        return base.should_subscribe_to_topic();
    }

    [Fact]
    public override Task should_receive_messages_via_listen_callback()
    {
        return base.should_receive_messages_via_listen_callback();
    }

    [Fact]
    public override Task should_fetch_topics()
    {
        return base.should_fetch_topics();
    }

    [Fact]
    public override Task should_shutdown_gracefully()
    {
        return base.should_shutdown_gracefully();
    }

    [Fact]
    public override Task should_handle_concurrent_message_processing()
    {
        return base.should_handle_concurrent_message_processing();
    }

    [Fact]
    public override Task should_dispose_without_exception()
    {
        return base.should_dispose_without_exception();
    }

    [Fact]
    public override Task should_have_valid_broker_address()
    {
        return base.should_have_valid_broker_address();
    }

    [Fact]
    public override Task should_invoke_log_callback_on_events()
    {
        return base.should_invoke_log_callback_on_events();
    }
}
