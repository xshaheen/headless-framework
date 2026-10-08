// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Headless.Messaging.Nats;

internal sealed class NatsTransport(
    ILogger<NatsTransport> logger,
    INatsConnectionPool connectionPool,
    NatsStreamProvisioner streamProvisioner,
    MessageLane lane = MessageLane.Bus
) : IBusTransport, IQueueTransport
{
    // Set once DisposeAsync runs: a later send fails instead of reaching the broker.
    private int _disposed;

    // Streams already warned about, so a publisher with no consumer logs once per stream rather than per message.
    private readonly ConcurrentDictionary<string, byte> _warnedNoConsumer = new(StringComparer.Ordinal);

    public BrokerAddress BrokerAddress => new("nats", connectionPool.ServersAddress);

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return OperateResult.Failed(new ObjectDisposedException(nameof(NatsTransport)));
        }

        MessagingRoutingAffinityMapping.RejectUnsupported(message, "Nats");
        string? subject = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var connection = connectionPool.GetConnection();
            // NatsJSContext is a stateless wrapper around the connection, so it's safe to create per call.
            var js = new NatsJSContext(connection);

            subject = ResolveSubject(message, lane, logger);

            // A publish-only host creates the stream itself rather than waiting for a consumer host to have started.
            var stream = await streamProvisioner
                .EnsureForPublishAsync(js, lane, message.Name, _IsSharded(message), cancellationToken)
                .ConfigureAwait(false);

            _WarnIfNoConsumer(stream);

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
                return _FailNoStream(message.Name, subject, "was not acknowledged by any stream (seq=0)", inner: null);
            }

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogNatsStreamMessagePublished(message.Name, ack.Seq);
            }

            return OperateResult.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Don't wrap the caller's cancellation as a publish failure. Any other cancellation, such as the
            // StreamCreateTimeout that bounds a publish-time stream ensure, is a failed send the outbox retries.
            throw;
        }
        catch (NatsJSPublishNoResponseException ex)
        {
            // NATS.Net retries a publish no stream answers, then throws this rather than returning a seq=0 ack.
            return _FailNoStream(
                message.Name,
                subject ?? NatsPhysicalAddress.Subject(lane, message.Name),
                "got no response from any stream",
                ex
            );
        }
        catch (Exception ex)
        {
            return OperateResult.Failed(new PublisherSentFailedException(ex.Message, ex));
        }
    }

    public ValueTask DisposeAsync()
    {
        Volatile.Write(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }

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

    /// <summary>
    /// Reads received NATS headers into transport headers, keeping the first value of a repeated header.
    /// </summary>
    internal static Dictionary<string, string?> ReadHeaders(NatsHeaders? natsHeaders)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (natsHeaders is { Count: > 0 })
        {
            foreach (var (key, values) in natsHeaders)
            {
                headers[key] = values.Count > 0 ? values[0] : null;
            }
        }

        return headers;
    }

    internal static NatsJSPubOpts CreatePublishOpts(TransportMessage message)
    {
        return new NatsJSPubOpts { MsgId = message.Id };
    }

    // The stream was deleted or changed after this process ensured it, so forget the ensure: the next publish of the
    // message provisions the stream again instead of failing until the process restarts.
    private OperateResult _FailNoStream(string messageName, string subject, string outcome, Exception? inner)
    {
        streamProvisioner.ForgetPublished(lane, messageName);

        var message =
            $"NATS JetStream publish to subject '{subject}' {outcome}; "
            + (
                streamProvisioner.IsEnabled
                    ? $"stream '{streamProvisioner.StreamName(lane, messageName)}' no longer captures the subject; the next publish provisions it again."
                    : $"StreamProvisioning is Disabled, so a JetStream stream (Headless would name it '{streamProvisioner.StreamName(lane, messageName)}') must be configured outside the application to capture this subject."
            );

        return OperateResult.Failed(new PublisherSentFailedException(message, inner));
    }

    // An interest-retention stream (every derived Bus stream) discards a message no consumer exists for when it is
    // published, and a Bus consumer created later starts at new messages anyway, so a publisher that starts before its
    // consumers loses those messages without an error.
    private void _WarnIfNoConsumer(NatsStreamState? stream)
    {
        if (
            stream is { Retention: StreamConfigRetention.Interest, ConsumerCount: 0 } state
            && _warnedNoConsumer.TryAdd(state.Stream, 0)
        )
        {
            logger.LogNatsInterestStreamWithoutConsumer(state.Stream);
        }
    }

    private static bool _IsSharded(TransportMessage message) =>
        message.Headers.TryGetValue(NatsMessagingHeaders.SubjectShard, out var shard)
        && !string.IsNullOrWhiteSpace(shard);

    internal static string ResolveSubject(
        TransportMessage message,
        MessageLane lane = MessageLane.Bus,
        ILogger? logger = null
    )
    {
        if (
            !message.Headers.TryGetValue(NatsMessagingHeaders.SubjectShard, out var shard)
            || string.IsNullOrWhiteSpace(shard)
        )
        {
            return NatsPhysicalAddress.Subject(lane, message.Name);
        }

        try
        {
            return NatsPhysicalAddress.Subject(lane, $"{message.Name}.{NatsSubjectShard.Validate(shard)}");
        }
        catch (InvalidOperationException ex)
        {
            logger?.LogInvalidSubjectShard(shard, ex.Message);
            return NatsPhysicalAddress.Subject(lane, message.Name);
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

    [LoggerMessage(
        EventId = 17,
        EventName = "NatsInterestStreamWithoutConsumer",
        Level = LogLevel.Warning,
        Message = "NATS stream '{Stream}' had no consumer when this process first published to it. It uses interest "
            + "retention, so a message published while no consumer exists is discarded, and a consumer created later "
            + "starts at new messages. Start Bus consumers before publishers, or send messages that must wait for a "
            + "consumer on the Queue lane."
    )]
    public static partial void LogNatsInterestStreamWithoutConsumer(this ILogger logger, string stream);
}
