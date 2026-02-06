// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.RabbitMq;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tests.Capabilities;

namespace Tests;

/// <summary>
/// Integration tests for RabbitMQ transport using real RabbitMQ container.
/// Inherits from <see cref="TransportTestsBase"/> to run standard transport tests.
/// </summary>
[Collection<RabbitMqFixture>]
public sealed class RabbitMqTransportTests(RabbitMqFixture fixture) : TransportTestsBase
{
    private IConnectionChannelPool? _connectionChannelPool;

    /// <inheritdoc />
    protected override TransportCapabilities Capabilities =>
        new()
        {
            SupportsOrdering = true,
            SupportsDeadLetter = true,
            SupportsPriority = false,
            SupportsDelayedDelivery = false,
            SupportsBatchSend = true,
            SupportsHeaders = true,
        };

    /// <inheritdoc />
    protected override ITransport GetTransport()
    {
        var logger = NullLogger<RabbitMqTransport>.Instance;
        _connectionChannelPool = _CreateConnectionChannelPool();

        return new RabbitMqTransport(logger, _connectionChannelPool);
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        if (_connectionChannelPool is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (_connectionChannelPool is IDisposable disposable)
        {
            disposable.Dispose();
        }

        await base.DisposeAsyncCore();
    }

    private IConnectionChannelPool _CreateConnectionChannelPool()
    {
        var messagingOptions = Options.Create(new MessagingOptions { Version = "v1" });
        var rabbitOptions = Options.Create(
            new RabbitMqOptions
            {
                HostName = fixture.HostName,
                Port = fixture.Port,
                UserName = fixture.UserName,
                Password = fixture.Password,
                ExchangeName = $"test-exchange-{Guid.NewGuid():N}",
            }
        );

        var logger = NullLogger<ConnectionChannelPool>.Instance;

        return new ConnectionChannelPool(logger, messagingOptions, rabbitOptions);
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
