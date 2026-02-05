// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using NATS.Client;
using NATS.Client.JetStream;

namespace Headless.Messaging.Nats;

internal class NatsTransport(ILogger<NatsTransport> logger, INatsConnectionPool connectionPool) : ITransport
{
    private readonly JetStreamOptions _jetStreamOptions = JetStreamOptions
        .Builder()
        .WithPublishNoAck(false)
        .WithRequestTimeout(3000)
        .Build();

    public BrokerAddress BrokerAddress => new("NATS", connectionPool.ServersAddress);

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        var connection = connectionPool.RentConnection();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var msg = new Msg(message.GetName(), message.Body.ToArray());
            foreach (var header in message.Headers)
            {
                msg.Header[header.Key] = header.Value;
            }

            var js = connection.CreateJetStreamContext(_jetStreamOptions);

            var builder = PublishOptions.Builder().WithMessageId(message.GetId());

            // Note: NATS .NET client doesn't support CancellationToken in PublishAsync yet
            var resp = await js.PublishAsync(msg, builder.Build()).ConfigureAwait(false);

            if (resp.Seq > 0)
            {
                logger.LogDebug("NATS stream message [{GetName}] has been published.", message.GetName());

                return OperateResult.Success;
            }

            throw new PublisherSentFailedException("NATS message send failed, no consumer reply!");
        }
        catch (Exception ex)
        {
            var warpEx = new PublisherSentFailedException(ex.Message, ex);

            return OperateResult.Failed(warpEx);
        }
        finally
        {
            connectionPool.Return(connection);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
