// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Nats;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tests.Capabilities;

namespace Tests;

[Collection("Nats")]
public sealed class NatsTransportTests(NatsFixture fixture) : TransportTestsBase
{
    private INatsConnectionPool? _connectionPool;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        // JetStream requires a stream to exist before publishing.
        // Create a catch-all stream for test subjects.
        // TransportTestsBase uses single-token subjects ("TestMessage", "TestMessageName").
        // NATS "*" matches any single token at one level.
        await fixture.EnsureStreamAsync("TEST", "*");
    }

    protected override TransportCapabilities Capabilities =>
        new()
        {
            SupportsOrdering = true,
            SupportsDeadLetter = false,
            SupportsPriority = false,
            SupportsDelayedDelivery = false,
            SupportsBatchSend = true,
            SupportsHeaders = true,
        };

    protected override IBusTransport GetBusTransport()
    {
        var natsOptions = Options.Create(
            new NatsMessagingOptions
            {
                Servers = fixture.ConnectionString,
                ConnectionPoolSize = 2,
                ConfigureConnection = opts => opts with { ConnectTimeout = TimeSpan.FromSeconds(10) },
            }
        );

        _connectionPool = new NatsConnectionPool(NullLogger<NatsConnectionPool>.Instance, natsOptions);

        return new NatsTransport(NullLogger<NatsTransport>.Instance, _connectionPool);
    }

    protected override IQueueTransport GetQueueTransport()
    {
        return GetBusTransport() as IQueueTransport
            ?? throw new InvalidOperationException("NATS transport must support queue intent.");
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_connectionPool is not null)
        {
            await _connectionPool.DisposeAsync();
        }

        await base.DisposeAsyncCore();
    }

    #region Transport Tests

    [Fact]
    public override Task should_send_message_successfully()
    {
        return base.should_send_message_successfully();
    }

    [Fact]
    public override Task should_have_valid_broker_address()
    {
        return base.should_have_valid_broker_address();
    }

    [Fact]
    public override Task should_include_headers_in_sent_message()
    {
        return base.should_include_headers_in_sent_message();
    }

    [Fact]
    public override Task should_send_batch_of_messages()
    {
        return base.should_send_batch_of_messages();
    }

    [Fact]
    public override Task should_handle_empty_message_body()
    {
        return base.should_handle_empty_message_body();
    }

    [Fact]
    public override Task should_handle_large_message_body()
    {
        return base.should_handle_large_message_body();
    }

    [Fact]
    public override Task should_dispose_async_without_exception()
    {
        return base.should_dispose_async_without_exception();
    }

    [Fact]
    public override Task should_handle_concurrent_sends()
    {
        return base.should_handle_concurrent_sends();
    }

    [Fact]
    public override Task should_include_message_id_in_headers()
    {
        return base.should_include_message_id_in_headers();
    }

    [Fact]
    public override Task should_include_message_name_in_headers()
    {
        return base.should_include_message_name_in_headers();
    }

    [Fact]
    public override Task should_handle_special_characters_in_message_body()
    {
        return base.should_handle_special_characters_in_message_body();
    }

    [Fact]
    public override Task should_handle_null_header_values()
    {
        return base.should_handle_null_header_values();
    }

    [Fact]
    public override Task should_handle_correlation_id_header()
    {
        return base.should_handle_correlation_id_header();
    }

    #endregion
}
