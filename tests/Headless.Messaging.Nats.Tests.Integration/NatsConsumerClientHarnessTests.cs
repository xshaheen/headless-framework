// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

[Collection("Nats")]
public sealed class NatsConsumerClientHarnessTests(NatsFixture fixture) : ConsumerClientTestsBase
{
    private readonly IServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_serviceProvider is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }

        await base.DisposeAsyncCore();
    }

    protected override async Task<IConsumerClient> GetConsumerClientAsync()
    {
        var client = new NatsConsumerClient(
            "test-group",
            1,
            Options.Create(
                new MessagingNatsOptions
                {
                    Servers = fixture.ConnectionString,
                    EnableSubscriberClientStreamAndSubjectCreation = false,
                }
            ),
            _serviceProvider
        );

        await client.ConnectAsync();
        return client;
    }

    [Fact]
    public override Task should_subscribe_to_topic() => base.should_subscribe_to_topic();

    [Fact]
    public override Task should_receive_messages_via_listen_callback() =>
        base.should_receive_messages_via_listen_callback();

    [Fact]
    public override Task should_commit_message_successfully() => base.should_commit_message_successfully();

    [Fact]
    public override Task should_reject_message_successfully() => base.should_reject_message_successfully();

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

    [Fact]
    public override Task should_invoke_log_callback_on_events() => base.should_invoke_log_callback_on_events();
}
