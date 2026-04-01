// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.RabbitMq;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

[Collection<RabbitMqFixture>]
public sealed class RabbitMqConsumerClientHarnessTests(RabbitMqFixture fixture) : ConsumerClientTestsBase
{
    private readonly IServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();
    private readonly List<ConnectionChannelPool> _connectionPools = [];

    protected override IConsumerClient GetConsumerClient()
    {
        var messagingOptions = Options.Create(new MessagingOptions { Version = "v1" });
        var rabbitOptions = Options.Create(
            new RabbitMqOptions
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

        return new RabbitMqConsumerClient($"test-group-{Guid.NewGuid():N}", 1, pool, rabbitOptions, _serviceProvider);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var pool in _connectionPools)
        {
            await pool.DisposeAsync();
        }

        _connectionPools.Clear();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public override Task should_subscribe_to_topic() => base.should_subscribe_to_topic();

    [Fact]
    public override Task should_receive_messages_via_listen_callback() =>
        base.should_receive_messages_via_listen_callback();

    [Fact(Skip = "RabbitMQ commit requires a real delivery tag from a broker-delivered message.")]
    public override Task should_commit_message_successfully() => Task.CompletedTask;

    [Fact(Skip = "RabbitMQ reject requires a real delivery tag from a broker-delivered message.")]
    public override Task should_reject_message_successfully() => Task.CompletedTask;

    [Fact]
    public override Task should_fetch_topics() => base.should_fetch_topics();

    [Fact]
    public override Task should_shutdown_gracefully() => base.should_shutdown_gracefully();

    [Fact]
    public override Task should_handle_concurrent_message_processing() =>
        base.should_handle_concurrent_message_processing();

    [Fact]
    public override Task should_dispose_without_exception() => base.should_dispose_without_exception();

    [Fact]
    public override Task should_have_valid_broker_address() => base.should_have_valid_broker_address();

    [Fact(Skip = "RabbitMQ commit requires a real delivery tag from a broker-delivered message.")]
    public override Task should_handle_null_sender_in_commit() => Task.CompletedTask;

    [Fact(Skip = "RabbitMQ reject requires a real delivery tag from a broker-delivered message.")]
    public override Task should_handle_null_sender_in_reject() => Task.CompletedTask;

    [Fact]
    public override Task should_invoke_log_callback_on_events() => base.should_invoke_log_callback_on_events();
}
