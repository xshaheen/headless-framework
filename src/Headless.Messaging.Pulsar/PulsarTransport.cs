// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Pulsar;

internal sealed class PulsarTransport(
    ILogger<PulsarTransport> logger,
    IConnectionFactory connectionFactory,
    MessageLane lane = MessageLane.Bus
) : IBusTransport, IQueueTransport
{
    private readonly ILogger _logger = logger;

    // Set once DisposeAsync runs: a later send fails instead of reaching the broker.
    private int _disposed;

    public BrokerAddress BrokerAddress => new("pulsar", connectionFactory.ServersAddress);

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return OperateResult.Failed(new ObjectDisposedException(nameof(PulsarTransport)));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = PulsarRoutingAffinity.Mapping.ResolveKey(message);
            var producer = await connectionFactory
                .CreateProducerAsync(PulsarPhysicalAddress.Topic(lane, message.Name))
                .ConfigureAwait(false);
            var headerDic = new Dictionary<string, string?>(message.Headers, StringComparer.Ordinal);
            var pulsarMessage = producer.NewMessage(TransportBody.ToArray(message.Body), key, headerDic);
            var messageId = await producer.SendAsync(pulsarMessage).ConfigureAwait(false);

            if (messageId != null)
            {
                var messageName = message.Name;
                _logger.MessagePublished(messageName);

                return OperateResult.Success;
            }

            throw new PublisherSentFailedException("pulsar message persisted failed!");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var wrapperEx = new PublisherSentFailedException(ex.Message, ex);

            return IsBrokerUnreachable(ex)
                ? OperateResult.BrokerUnreachable(wrapperEx)
                : OperateResult.Failed(wrapperEx);
        }
    }

    // A lost connection or a broker that does not answer counts; a closed producer or a failed topic lookup can be this
    // message's own problem, so it does not.
    internal static bool IsBrokerUnreachable(Exception exception) =>
        exception
            is global::Pulsar.Client.Api.ConnectException
                or global::Pulsar.Client.Api.NotConnectedException
                or global::Pulsar.Client.Api.ConnectionFailedOnSend
                or global::Pulsar.Client.Api.TimeoutException
                or global::Pulsar.Client.Api.RequestTimeoutException
        || BrokerFailures.IsConnectivity(exception);

    public ValueTask DisposeAsync()
    {
        Volatile.Write(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }
}

internal static partial class PulsarTransportLog
{
    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Debug,
        Message = "pulsar topic message [{GetName}] has been published."
    )]
    public static partial void MessagePublished(this ILogger logger, string getName);
}
