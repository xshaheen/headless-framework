// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;

namespace Headless.Messaging.Nats;

internal sealed class NatsTransport(ILogger<NatsTransport> logger, INatsConnectionPool connectionPool) : ITransport
{
    public BrokerAddress BrokerAddress => new("nats", connectionPool.ServersAddress);

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var connection = connectionPool.GetConnection();
            // NatsJSContext is a stateless wrapper around the connection — safe to create per call
            var js = new NatsJSContext(connection);

            var ack = await js.PublishAsync(
                    subject: message.GetName(),
                    data: message.Body,
                    serializer: NatsRawSerializer<ReadOnlyMemory<byte>>.Default,
                    opts: CreatePublishOpts(message),
                    headers: CreatePublishHeaders(message),
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            if (ack.Error is not null)
            {
                return OperateResult.Failed(
                    new PublisherSentFailedException($"NATS publish error {ack.Error.Code}: {ack.Error.Description}")
                );
            }

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("NATS stream message [{Name}] published, seq={Seq}.", message.GetName(), ack.Seq);
            }

            return OperateResult.Success;
        }
        catch (OperationCanceledException)
        {
            // Don't wrap cancellation as a publish failure
            throw;
        }
        catch (Exception ex)
        {
            return OperateResult.Failed(new PublisherSentFailedException(ex.Message, ex));
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    internal static NatsHeaders? CreatePublishHeaders(TransportMessage message)
    {
        NatsHeaders? headers = null;
        foreach (var header in message.Headers)
        {
            if (header.Value is not null)
            {
                headers ??= new NatsHeaders();
                headers[header.Key] = header.Value;
            }
        }

        return headers;
    }

    internal static NatsJSPubOpts CreatePublishOpts(TransportMessage message)
    {
        return new NatsJSPubOpts { MsgId = message.GetId() };
    }
}
