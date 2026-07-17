// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Messaging.InMemory;

/// <summary>
/// Transport implementation for in-memory message queue.
/// </summary>
internal sealed class InMemoryQueueTransport(MemoryQueue queue, ILogger<InMemoryQueueTransport> logger)
    : IQueueTransport
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
    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        var messageName = message.Name;
        var result = await InMemoryTransportCore
            .SendCoreAsync(message, queue.SendQueue, cancellationToken)
            .ConfigureAwait(false);

        if (result.Succeeded)
        {
            _logger.QueueMessagePublished(messageName);
        }

        return result;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

internal static partial class InMemoryQueueTransportLog
{
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Debug,
        Message = "Event message [{MessageName}] has been published."
    )]
    public static partial void QueueMessagePublished(this ILogger logger, string messageName);
}
