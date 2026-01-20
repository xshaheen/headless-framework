using Framework.Messages.Internal;
using Framework.Messages.Messages;
using Framework.Messages.Transport;
using Microsoft.Extensions.Logging;

namespace Framework.Messages;

/// <summary>
/// Transport implementation for in-memory message queue.
/// </summary>
internal sealed class InMemoryQueueTransport(InMemoryQueue queue, ILogger<InMemoryQueueTransport> logger) : ITransport
{
    private readonly ILogger _logger = logger;

    /// <summary>
    /// Gets the broker address information.
    /// </summary>
    public BrokerAddress BrokerAddress => new("InMemory", "localhost");

    /// <summary>
    /// Sends a transport message asynchronously.
    /// </summary>
    /// <param name="message">The transport message to send</param>
    /// <returns>A task that returns the operation result</returns>
    public Task<OperateResult> SendAsync(TransportMessage message)
    {
        try
        {
            queue.Send(message);
            _logger.LogDebug("Event message [{MessageName}] has been published.", message.GetName());
            return Task.FromResult(OperateResult.Success);
        }
        catch (Exception e)
        {
            var wrapperEx = new PublisherSentFailedException(e.Message, e);
            return Task.FromResult(OperateResult.Failed(wrapperEx));
        }
    }
}
