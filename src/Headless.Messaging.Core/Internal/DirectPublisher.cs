// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Messages;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

internal sealed class DirectPublisher(
    ISerializer serializer,
    ITransport transport,
    ILongIdGenerator idGenerator,
    TimeProvider timeProvider,
    IOptions<MessagingOptions> options
) : IDirectPublisher
{
    private static readonly DiagnosticListener _DiagnosticListener = new(
        MessageDiagnosticListenerNames.DiagnosticListenerName
    );

    /// <summary>
    /// Cache for fully-resolved topic names (including prefix) keyed by message type.
    /// Instance-level because prefix varies per MessagingOptions configuration.
    /// Eliminates per-publish string allocations in high-throughput scenarios.
    /// </summary>
    private readonly ConcurrentDictionary<Type, string> _topicNameCache = new();

    private readonly ISerializer _serializer = serializer;
    private readonly ITransport _transport = transport;
    private readonly ILongIdGenerator _idGenerator = idGenerator;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly MessagingOptions _options = options.Value;

    public Task PublishAsync<T>(T contentObj, CancellationToken cancellationToken = default)
        where T : class
    {
        return _PublishCoreAsync(contentObj, headers: null, cancellationToken);
    }

    public Task PublishAsync<T>(
        T contentObj,
        IDictionary<string, string?> headers,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        return _PublishCoreAsync(contentObj, headers, cancellationToken);
    }

    private async Task _PublishCoreAsync<T>(
        T contentObj,
        IDictionary<string, string?>? headers,
        CancellationToken cancellationToken
    )
        where T : class
    {
        Argument.IsNotNull(contentObj);

        cancellationToken.ThrowIfCancellationRequested();

        // Pre-size for: MessageId, CorrelationId, CorrelationSequence, MessageName, SentTime (+ caller headers)
        headers ??= new Dictionary<string, string?>(capacity: 6, StringComparer.Ordinal);

        // Resolve topic from type mapping (cached with prefix)
        var name = _GetTopicName<T>();

        // Generate standard headers
        _GenerateHeaders(headers, name);

        var message = new Message(headers, contentObj);

        await _SendAsync(message, cancellationToken).AnyContext();
    }

    private string _GetTopicName<T>()
        where T : class
    {
        var messageType = typeof(T);

        // Check cache first (includes prefix)
        if (_topicNameCache.TryGetValue(messageType, out var cachedName))
        {
            return cachedName;
        }

        // Resolve and cache
        var topicName = _ResolveTopicName(messageType);

        // Apply prefix if configured
        if (!string.IsNullOrEmpty(_options.TopicNamePrefix))
        {
            topicName = string.Concat(_options.TopicNamePrefix, ".", topicName);
        }

        // Cache includes prefix for zero-allocation on subsequent publishes
        _topicNameCache.TryAdd(messageType, topicName);

        return topicName;
    }

    private string _ResolveTopicName(Type messageType)
    {
        // Check explicit topic mappings first
        if (_options.TopicMappings.TryGetValue(messageType, out var topicName))
        {
            return topicName;
        }

        // Check conventions
        if (_options.Conventions?.GetTopicName(messageType) is { } conventionTopic)
        {
            return conventionTopic;
        }

        throw new InvalidOperationException(
            $"No topic mapping found for message type '{messageType.Name}'. "
                + $"Register a topic mapping using WithTopicMapping<{messageType.Name}>(\"topic-name\") "
                + "in your messaging configuration."
        );
    }

    private async Task _SendAsync(Message message, CancellationToken cancellationToken)
    {
        TransportMessage transportMsg;
        try
        {
            transportMsg = await _serializer.SerializeToTransportMessageAsync(message).AnyContext();
        }
        catch (Exception e)
        {
            _TracingErrorSerialization(message, e);
            throw;
        }

        long? tracingTimestamp = null;
        try
        {
            tracingTimestamp = _TracingBeforeSend(transportMsg);

            var result = await _transport.SendAsync(transportMsg, cancellationToken).AnyContext();

            if (!result.Succeeded)
            {
                _TracingErrorSend(tracingTimestamp, transportMsg, result);
                throw new Headless.Messaging.PublisherSentFailedException(result.ToString(), result.Exception);
            }

            _TracingAfterSend(tracingTimestamp, transportMsg);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected behavior, not an error - let it propagate without tracing
            throw;
        }
        catch (Exception e) when (e is not Headless.Messaging.PublisherSentFailedException)
        {
            try
            {
                _TracingErrorSend(tracingTimestamp, transportMsg, e);
            }
#pragma warning disable ERP022 // Intentional: tracing failure should not mask the original exception
            catch
            {
                // Tracing failure should not mask the original exception
            }
#pragma warning restore ERP022

            throw;
        }
    }

    private void _GenerateHeaders(IDictionary<string, string?> headers, string name)
    {
        if (!headers.TryGetValue(Headers.MessageId, out var messageId) || string.IsNullOrEmpty(messageId))
        {
            messageId = _idGenerator.Create().ToString(CultureInfo.InvariantCulture);
            headers[Headers.MessageId] = messageId;
        }

        if (!headers.ContainsKey(Headers.CorrelationId))
        {
            headers[Headers.CorrelationId] = messageId;
            headers[Headers.CorrelationSequence] = "0";
        }

        headers[Headers.MessageName] = name;
        headers[Headers.SentTime] = _timeProvider.GetUtcNow().UtcDateTime.ToString(CultureInfo.InvariantCulture);
    }

    #region Tracing

    private long? _TracingBeforeSend(TransportMessage message)
    {
        MessageEventCounterSource.Log.WritePublishMetrics();

        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.BeforePublish))
        {
            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Operation = message.GetName(),
                BrokerAddress = _transport.BrokerAddress,
                TransportMessage = message,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.BeforePublish, eventData);

            return eventData.OperationTimestamp;
        }

        return null;
    }

    private void _TracingAfterSend(long? tracingTimestamp, TransportMessage message)
    {
        if (tracingTimestamp != null && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.AfterPublish))
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                BrokerAddress = _transport.BrokerAddress,
                TransportMessage = message,
                ElapsedTimeMs = now - tracingTimestamp.Value,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.AfterPublish, eventData);
        }
    }

    private void _TracingErrorSend(long? tracingTimestamp, TransportMessage message, OperateResult result)
    {
        if (tracingTimestamp != null && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorPublish))
        {
            var ex = new Headless.Messaging.PublisherSentFailedException(result.ToString(), result.Exception);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                BrokerAddress = _transport.BrokerAddress,
                TransportMessage = message,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                Exception = ex,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorPublish, eventData);
        }
    }

    private void _TracingErrorSend(long? tracingTimestamp, TransportMessage message, Exception exception)
    {
        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorPublish))
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var eventData = new MessageEventDataPubSend
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                BrokerAddress = _transport.BrokerAddress,
                TransportMessage = message,
                ElapsedTimeMs = tracingTimestamp.HasValue ? now - tracingTimestamp.Value : null,
                Exception = exception,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorPublish, eventData);
        }
    }

    private static void _TracingErrorSerialization(Message message, Exception exception)
    {
        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorPublishMessageStore))
        {
            var eventData = new MessageEventDataPubStore
            {
                OperationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Operation = message.GetName(),
                Message = message,
                Exception = exception,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorPublishMessageStore, eventData);
        }
    }

    #endregion
}
