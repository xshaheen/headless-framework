// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using Headless.Messaging.Serialization;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Testing.Internal;

internal sealed class RecordingBusTransport(
    IBusTransport inner,
    MessageObservationStore store,
    ISerializer serializer,
    ILogger<RecordingBusTransport>? logger = null
) : IBusTransport
{
    public BrokerAddress BrokerAddress => inner.BrokerAddress;

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        OperateResult result;
        using (RecordingTransportRecorder.SuppressNestedRecording())
        {
            result = await inner.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }

        await RecordingTransportRecorder
            .RecordPublishedAsync(result, message, IntentType.Bus, store, serializer, logger)
            .ConfigureAwait(false);
        return result;
    }

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}

internal sealed class RecordingQueueTransport(
    IQueueTransport inner,
    MessageObservationStore store,
    ISerializer serializer,
    ILogger<RecordingQueueTransport>? logger = null
) : IQueueTransport
{
    public BrokerAddress BrokerAddress => inner.BrokerAddress;

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        OperateResult result;
        using (RecordingTransportRecorder.SuppressNestedRecording())
        {
            result = await inner.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }

        await RecordingTransportRecorder
            .RecordPublishedAsync(result, message, IntentType.Queue, store, serializer, logger)
            .ConfigureAwait(false);
        return result;
    }

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}

internal static class RecordingTransportRecorder
{
    private static readonly ConcurrentDictionary<string, Type?> _TypeCache = new(StringComparer.Ordinal);
    private static readonly AsyncLocal<int> _NestedRecordingSuppression = new();

    public static IDisposable SuppressNestedRecording()
    {
        _NestedRecordingSuppression.Value++;
        return new SuppressionScope();
    }

    public static async Task RecordPublishedAsync(
        OperateResult result,
        TransportMessage message,
        IntentType intentType,
        MessageObservationStore store,
        ISerializer serializer,
        ILogger? logger
    )
    {
        if (_NestedRecordingSuppression.Value > 0)
        {
            return;
        }

        if (!result.Succeeded)
        {
            return;
        }

        var headers = message.Headers;
        var messageTypeName = headers.TryGetValue(Headers.Type, out var typeName) ? typeName : null;

        object messageObj = message;
        var messageType = typeof(TransportMessage);

        if (message.Body.Length > 0 && messageTypeName != null)
        {
            var resolvedType = _ResolveType(messageTypeName);

            if (resolvedType != null)
            {
                try
                {
                    var deserialized = await serializer.DeserializeAsync(message, resolvedType).ConfigureAwait(false);

                    if (deserialized.Value != null)
                    {
                        messageObj = deserialized.Value;
                        messageType = resolvedType;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
                {
                    logger?.LogDeserializeObservedPayloadFailed(ex, resolvedType.FullName);
                }
            }
        }

        var recorded = RecordedMessage.FromHeaders(headers, messageObj, messageType, store.GetUtcNow(), intentType);
        store.Record(recorded, MessageObservationType.Published);
    }

    private static Type? _ResolveType(string typeName) =>
        _TypeCache.GetOrAdd(
            typeName,
            static name =>
            {
                var type = Type.GetType(name);
                if (type != null)
                {
                    return type;
                }

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.IsDynamic)
                    {
                        continue;
                    }

                    try
                    {
                        foreach (var candidate in assembly.GetExportedTypes())
                        {
                            if (candidate.FullName == name || candidate.Name == name)
                            {
                                return candidate;
                            }
                        }
                    }
                    catch (ReflectionTypeLoadException)
                    {
                        // Some assemblies may fail to load types — skip them
                    }
                }

                return null;
            }
        );

    private sealed class SuppressionScope : IDisposable
    {
        public void Dispose()
        {
            _NestedRecordingSuppression.Value--;
        }
    }
}

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
