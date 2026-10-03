// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Headless.Messaging.Serialization;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Testing.Internal;

internal static partial class RecordingTransportLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "DeserializeObservedPayloadFailed",
        Level = LogLevel.Warning,
        Message = "RecordingTransport failed to deserialize observed payload as {MessageType}; falling back to TransportMessage. WaitForPublished<{MessageType}> will time out."
    )]
    public static partial void LogDeserializeObservedPayloadFailed(
        this ILogger logger,
        Exception exception,
        string? messageType
    );
}
