// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Headless.Messaging.Serialization;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Testing.Internal;

internal sealed class RecordingBusTransport(
    IBusTransport inner,
    MessageObservationStore store,
    IMessageSerializer serializer,
    ILogger<RecordingBusTransport>? logger = null
) : IBusTransport
{
    public BrokerAddress BrokerAddress => inner.BrokerAddress;

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        OperateResult result;
        RecordingTransportRecorder.StampResetGeneration(message, store);
        using (RecordingTransportRecorder.SuppressNestedRecording())
        {
            result = await inner.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }

        await RecordingTransportRecorder
            .RecordPublishedAsync(result, message, MessageLane.Bus, store, serializer, logger)
            .ConfigureAwait(false);
        return result;
    }

    public ValueTask DisposeAsync()
    {
        return inner.DisposeAsync();
    }
}
