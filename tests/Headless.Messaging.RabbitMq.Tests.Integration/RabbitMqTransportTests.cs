// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.RabbitMq;
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
            SupportsBusTransport = true,
            SupportsQueueTransport = true,
            SupportsHeaders = true,
        };

    /// <inheritdoc />
    protected override IBusTransport GetBusTransport()
    {
        var logger = NullLogger<RabbitMqTransport>.Instance;
        _connectionChannelPool = _CreateConnectionChannelPool();

        return new RabbitMqTransport(logger, _connectionChannelPool);
    }

    protected override IQueueTransport GetQueueTransport()
    {
        return GetBusTransport() as IQueueTransport
            ?? throw new InvalidOperationException("RabbitMQ transport must support queue intent.");
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
            new RabbitMqMessagingOptions
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
    public override Task should_accept_message_with_application_headers()
    {
        return base.should_accept_message_with_application_headers();
    }

    [Fact]
    public override Task should_send_multiple_messages_individually()
    {
        return base.should_send_multiple_messages_individually();
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
    public override Task should_accept_message_with_id()
    {
        return base.should_accept_message_with_id();
    }

    [Fact]
    public override Task should_accept_message_with_name()
    {
        return base.should_accept_message_with_name();
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
