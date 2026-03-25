// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Nats;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tests.Capabilities;

namespace Tests;

[Collection("Nats")]
public sealed class NatsTransportTests : TransportTestsBase, IAsyncLifetime
{
    private readonly NatsFixture _fixture;
    private INatsConnectionPool? _connectionPool;

    public NatsTransportTests(NatsFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        // JetStream requires a stream to exist before publishing.
        // Create a catch-all stream for test subjects.
        await _fixture.EnsureStreamAsync("TEST", ">");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

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

    protected override ITransport GetTransport()
    {
        var natsOptions = Options.Create(
            new MessagingNatsOptions { Servers = _fixture.ConnectionString, ConnectionPoolSize = 2 }
        );

        _connectionPool = new NatsConnectionPool(NullLogger<NatsConnectionPool>.Instance, natsOptions);

        return new NatsTransport(NullLogger<NatsTransport>.Instance, _connectionPool);
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
    public override Task should_send_message_successfully() => base.should_send_message_successfully();

    [Fact]
    public override Task should_have_valid_broker_address() => base.should_have_valid_broker_address();

    [Fact]
    public override Task should_include_headers_in_sent_message() => base.should_include_headers_in_sent_message();

    [Fact]
    public override Task should_send_batch_of_messages() => base.should_send_batch_of_messages();

    [Fact]
    public override Task should_handle_empty_message_body() => base.should_handle_empty_message_body();

    [Fact]
    public override Task should_handle_large_message_body() => base.should_handle_large_message_body();

    [Fact]
    public override Task should_maintain_message_ordering() => base.should_maintain_message_ordering();

    [Fact]
    public override Task should_dispose_async_without_exception() => base.should_dispose_async_without_exception();

    [Fact]
    public override Task should_handle_concurrent_sends() => base.should_handle_concurrent_sends();

    [Fact]
    public override Task should_include_message_id_in_headers() => base.should_include_message_id_in_headers();

    [Fact]
    public override Task should_include_message_name_in_headers() => base.should_include_message_name_in_headers();

    [Fact]
    public override Task should_handle_special_characters_in_message_body() =>
        base.should_handle_special_characters_in_message_body();

    [Fact]
    public override Task should_handle_null_header_values() => base.should_handle_null_header_values();

    [Fact]
    public override Task should_handle_correlation_id_header() => base.should_handle_correlation_id_header();

    #endregion
}
