// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using DotPulsar;
using DotPulsar.Abstractions;
using DotPulsar.Exceptions;
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
            var producer = connectionFactory.GetProducer(PulsarPhysicalAddress.Topic(lane, message.Name));
            var metadata = new MessageMetadata();
            if (key is not null)
            {
                metadata.Key = key;
            }

            foreach (var header in message.Headers)
            {
                // A Pulsar property always has a value, so a header without one is left off, as a reader treats both
                // the same way.
                if (header.Value is not null)
                {
                    metadata[header.Key] = header.Value;
                }
            }

            await SendAsync(
                    producer,
                    connectionFactory.SizeRefusals,
                    metadata,
                    new ReadOnlySequence<byte>(message.Body),
                    connectionFactory.SendTimeout,
                    cancellationToken
                )
                .ConfigureAwait(false);

            _logger.MessagePublished(message.Name);

            return OperateResult.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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

    /// <summary>
    /// Sends one message and waits for the broker to store it, failing with <see cref="TimeoutException"/> once
    /// <paramref name="sendTimeout"/> passes; <see cref="TimeSpan.Zero"/> waits indefinitely.
    /// </summary>
    /// <remarks>
    /// DotPulsar keeps a send pending through a lost connection and retries it after reconnecting, so the timeout is what
    /// bounds a publish while the broker is away.
    /// </remarks>
    internal static async Task SendAsync(
        IProducer<ReadOnlySequence<byte>> producer,
        PulsarSizeRefusals? sizeRefusals,
        MessageMetadata metadata,
        ReadOnlySequence<byte> payload,
        TimeSpan sendTimeout,
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (sendTimeout > TimeSpan.Zero)
        {
            timeout.CancelAfter(sendTimeout);
        }

        // DotPulsar faults the producer on a send it cannot make, such as a message larger than the broker accepts,
        // and leaves that send waiting; watching the state fails it at once instead of after the timeout. Only a
        // refusal of this message's size is the message's own problem; any other fault counts as the broker being
        // unreachable, as the timeout it replaces did, so a real outage still shows.
        var send = producer.Send(metadata, payload, timeout.Token).AsTask();
        var faulted = producer.State.OnStateChangeTo(ProducerState.Faulted, timeout.Token).AsTask();

        try
        {
            if (
                await Task.WhenAny(send, faulted).ConfigureAwait(false) == faulted
                && faulted.IsCompletedSuccessfully
                && await faulted.ConfigureAwait(false) == ProducerState.Faulted
            )
            {
                _ObserveAbandoned(send);

                throw sizeRefusals?.TryTake(payload.Length, out var refusal) == true
                    ? new ProducerFaultedException(refusal)
                    : new ProducerFaultedException(
                        new InvalidOperationException(
                            $"The producer for '{producer.Topic}' faulted during the send; the client log names the cause."
                        )
                    );
            }

            await send.ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The broker did not acknowledge the send to '{producer.Topic}' within {sendTimeout}.",
                e
            );
        }
        finally
        {
            // Stops the state watch of a send that completed; it never outlives this call.
            await timeout.CancelAsync().ConfigureAwait(false);
            _ObserveAbandoned(faulted);
        }
    }

    private static void _ObserveAbandoned(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    // A send that timed out (the client waits through a lost connection), a socket or I/O failure, or a faulted producer
    // counts. A producer faulted by this message's size, or a message the broker rejected, is the message's own
    // problem, so it does not.
    internal static bool IsBrokerUnreachable(Exception exception) =>
        BrokerFailures.IsConnectivity(exception)
        || exception is ProducerFaultedException { InnerException: not TooLargeMessageException };

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
    internal static partial void MessagePublished(this ILogger logger, string getName);
}
