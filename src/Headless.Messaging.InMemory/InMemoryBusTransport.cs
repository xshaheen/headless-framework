// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.InMemory;

/// <summary>
/// Transport implementation for in-memory message bus fan-out.
/// </summary>
internal sealed class InMemoryBusTransport(MemoryQueue queue, ILogger<InMemoryBusTransport> logger)
    : IBusTransport, ITransport
{
    private readonly ILogger _logger = logger;

    /// <summary>
    /// Gets the broker address information.
    /// </summary>
    public BrokerAddress BrokerAddress => new("InMemory", "localhost");

    /// <summary>
    /// Sends a transport message asynchronously.
    /// </summary>
    /// <param name="message">The transport message to send.</param>
    /// <param name="cancellationToken">Token to cancel the send operation.</param>
    /// <returns>A task that returns the operation result.</returns>
    public Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            queue.SendBus(message);
            var messageName = message.GetName();
            _logger.BusMessagePublished(messageName);
            return Task.FromResult(OperateResult.Success);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            var wrapperEx = new PublisherSentFailedException(e.Message, e);
            return Task.FromResult(OperateResult.Failed(wrapperEx));
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal static partial class InMemoryBusTransportLog
{
    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Debug,
        Message = "Bus message [{MessageName}] has been published."
    )]
    public static partial void BusMessagePublished(this ILogger logger, string messageName);
}
