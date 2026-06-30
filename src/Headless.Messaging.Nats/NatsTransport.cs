// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;

namespace Headless.Messaging.Nats;

internal sealed class NatsTransport(ILogger<NatsTransport> logger, INatsConnectionPool connectionPool)
    : IBusTransport,
        IQueueTransport
{
    public BrokerAddress BrokerAddress => new("nats", connectionPool.ServersAddress);

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var connection = connectionPool.GetConnection();
            // NatsJSContext is a stateless wrapper around the connection, so it's safe to create per call.
            var js = new NatsJSContext(connection);

            var subject = ResolveSubject(message, logger);

            var ack = await js.PublishAsync(
                    subject: subject,
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
                    new PublisherSentFailedException(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"NATS publish error {ack.Error.Code}: {ack.Error.Description}"
                        )
                    )
                );
            }

            if (ack.Seq == 0)
            {
                return OperateResult.Failed(
                    new PublisherSentFailedException(
                        $"NATS JetStream publish to subject '{subject}' was not acknowledged by any stream (seq=0); "
                            + "ensure a JetStream stream is configured to capture this subject."
                    )
                );
            }

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogNatsStreamMessagePublished(message.GetName(), ack.Seq);
            }

            return OperateResult.Success;
        }
        catch (OperationCanceledException)
        {
            // Don't wrap cancellation as a publish failure.
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
                headers ??= [];
                headers[header.Key] = header.Value;
            }
        }

        return headers;
    }

    internal static NatsJSPubOpts CreatePublishOpts(TransportMessage message)
    {
        return new NatsJSPubOpts { MsgId = message.GetId() };
    }

    internal static string ResolveSubject(TransportMessage message, ILogger? logger = null)
    {
        if (
            !message.Headers.TryGetValue(NatsMessagingHeaders.SubjectShard, out var shard)
            || string.IsNullOrWhiteSpace(shard)
        )
        {
            return message.GetName();
        }

        try
        {
            return $"{message.GetName()}.{NatsSubjectShard.Validate(shard)}";
        }
        catch (InvalidOperationException ex)
        {
            logger?.LogInvalidSubjectShard(shard, ex.Message);
            return message.GetName();
        }
    }
}

internal static partial class NatsTransportLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "NatsStreamMessagePublished",
        Level = LogLevel.Debug,
        Message = "NATS stream message [{Name}] published, seq={Seq}."
    )]
    public static partial void LogNatsStreamMessagePublished(this ILogger logger, string name, ulong seq);

    [LoggerMessage(
        EventId = 2,
        EventName = "NatsInvalidSubjectShard",
        Level = LogLevel.Warning,
        Message = "NATS SubjectShard '{Shard}' is invalid and will be ignored: {Reason}. Falling back to base subject."
    )]
    public static partial void LogInvalidSubjectShard(this ILogger logger, string shard, string reason);
}
