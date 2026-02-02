// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transactions;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

internal class OutboxPublisher(IServiceProvider service) : IOutboxPublisher
{
    // ReSharper disable once InconsistentNaming
    protected static DiagnosticListener DiagnosticListener { get; } =
        new(MessageDiagnosticListenerNames.DiagnosticListenerName);

    private readonly MessagingOptions _messagingOptions = service
        .GetRequiredService<IOptions<MessagingOptions>>()
        .Value;
    private readonly IDispatcher _dispatcher = service.GetRequiredService<IDispatcher>();
    private readonly IDataStorage _storage = service.GetRequiredService<IDataStorage>();
    private readonly ILongIdGenerator _longIdGenerator = service.GetRequiredService<ILongIdGenerator>();
    private readonly TimeProvider _timeProvider = service.GetRequiredService<TimeProvider>();

    private readonly AsyncLocal<OutboxTransactionHolder> _asyncLocal = new();

    public IServiceProvider ServiceProvider { get; } = service;

    public IOutboxTransaction? Transaction
    {
        get => _asyncLocal.Value?.Transaction;
        set
        {
            _asyncLocal.Value ??= new OutboxTransactionHolder();
            _asyncLocal.Value.Transaction = value;
        }
    }

    public async Task PublishAsync<T>(
        string name,
        T? value,
        IDictionary<string, string?> headers,
        CancellationToken cancellationToken = default
    )
    {
        await _PublishInternalAsync(name, value, headers, null, cancellationToken).AnyContext();
    }

    public async Task PublishAsync<T>(
        string name,
        T? value,
        string? callbackName = null,
        CancellationToken cancellationToken = default
    )
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            { Headers.CallbackName, callbackName },
        };

        await PublishAsync(name, value, headers, cancellationToken).AnyContext();
    }

    public async Task PublishDelayAsync<T>(
        TimeSpan delayTime,
        string name,
        T? value,
        IDictionary<string, string?> headers,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsPositive(delayTime);

        await _PublishInternalAsync(name, value, headers, delayTime, cancellationToken).AnyContext();
    }

    public async Task PublishDelayAsync<T>(
        TimeSpan delayTime,
        string name,
        T? value,
        string? callbackName = null,
        CancellationToken cancellationToken = default
    )
    {
        var header = new Dictionary<string, string?>(StringComparer.Ordinal) { { Headers.CallbackName, callbackName } };

        await PublishDelayAsync(delayTime, name, value, header, cancellationToken).AnyContext();
    }

    public Task PublishAsync<T>(
        T? contentObj,
        string? callbackName = null,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        var topicName = _GetTopicNameFromMapping<T>();
        return PublishAsync(topicName, contentObj, callbackName, cancellationToken);
    }

    public Task PublishAsync<T>(
        T? contentObj,
        IDictionary<string, string?> headers,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        var topicName = _GetTopicNameFromMapping<T>();
        return PublishAsync(topicName, contentObj, headers, cancellationToken);
    }

    public Task PublishDelayAsync<T>(
        TimeSpan delayTime,
        T? contentObj,
        IDictionary<string, string?> headers,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        var topicName = _GetTopicNameFromMapping<T>();
        return PublishDelayAsync(delayTime, topicName, contentObj, headers, cancellationToken);
    }

    public Task PublishDelayAsync<T>(
        TimeSpan delayTime,
        T? contentObj,
        string? callbackName = null,
        CancellationToken cancellationToken = default
    )
        where T : class
    {
        var topicName = _GetTopicNameFromMapping<T>();
        return PublishDelayAsync(delayTime, topicName, contentObj, callbackName, cancellationToken);
    }

    private string _GetTopicNameFromMapping<T>()
        where T : class
    {
        var messageType = typeof(T);

        // Check explicit topic mappings first
        if (_messagingOptions.TopicMappings.TryGetValue(messageType, out var topicName))
        {
            return topicName;
        }

        // Check conventions
        if (_messagingOptions.Conventions?.GetTopicName(messageType) is { } conventionTopic)
        {
            return conventionTopic;
        }

        throw new InvalidOperationException(
            $"No topic mapping found for message type '{messageType.Name}'. "
                + $"Register a topic mapping using WithTopicMapping<{messageType.Name}>(\"topic-name\") "
                + "or use the overload that accepts an explicit topic name."
        );
    }

    private async Task _PublishInternalAsync<T>(
        string name,
        T? value,
        IDictionary<string, string?> headers,
        TimeSpan? delayTime = null,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentNullException(nameof(name));
        }

        if (!string.IsNullOrEmpty(_messagingOptions.TopicNamePrefix))
        {
            name = $"{_messagingOptions.TopicNamePrefix}.{name}";
        }

        if (!headers.TryGetValue(Headers.MessageId, out var value1))
        {
            var messageId = _longIdGenerator.Create().ToString(CultureInfo.InvariantCulture);
            value1 = messageId;
            headers.Add(Headers.MessageId, value1);
        }

        if (!headers.ContainsKey(Headers.CorrelationId))
        {
            headers.Add(Headers.CorrelationId, value1);
            headers.Add(Headers.CorrelationSequence, 0.ToString());
        }

        headers.Add(Headers.MessageName, name);
        headers.Add(Headers.Type, typeof(T).Name);

        var publishTime = _timeProvider.GetUtcNow().UtcDateTime;
        if (delayTime != null)
        {
            publishTime += delayTime.Value;
            headers.Add(Headers.DelayTime, delayTime.Value.ToString());
            headers.Add(Headers.SentTime, publishTime.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            headers.Add(Headers.SentTime, publishTime.ToString(CultureInfo.InvariantCulture));
        }

        var message = new Message(headers, value);

        long? tracingTimestamp = null;
        try
        {
            tracingTimestamp = _TracingBefore(message);

            if (Transaction?.DbTransaction == null)
            {
                var mediumMessage = await _storage.StoreMessageAsync(name, message).AnyContext();

                _TracingAfter(tracingTimestamp, message);

                if (delayTime != null)
                {
                    await _dispatcher
                        .EnqueueToScheduler(mediumMessage, publishTime, null, cancellationToken)
                        .AnyContext();
                }
                else
                {
                    await _dispatcher.EnqueueToPublish(mediumMessage, cancellationToken).AnyContext();
                }
            }
            else
            {
                var transaction = (OutboxTransaction)Transaction;

                var mediumMessage = await _storage
                    .StoreMessageAsync(name, message, transaction.DbTransaction)
                    .AnyContext();

                _TracingAfter(tracingTimestamp, message);

                transaction.AddToSent(mediumMessage);

                if (transaction.AutoCommit)
                {
                    await transaction.CommitAsync(cancellationToken).AnyContext();
                }
            }
        }
        catch (Exception e)
        {
            _TracingError(tracingTimestamp, message, e);

            throw;
        }
    }

    #region tracing

    private static long? _TracingBefore(Message message)
    {
        if (DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.BeforePublishMessageStore))
        {
            var eventData = new MessageEventDataPubStore
            {
                OperationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Operation = message.GetName(),
                Message = message,
            };

            DiagnosticListener.Write(MessageDiagnosticListenerNames.BeforePublishMessageStore, eventData);

            return eventData.OperationTimestamp;
        }

        return null;
    }

    private static void _TracingAfter(long? tracingTimestamp, Message message)
    {
        if (
            tracingTimestamp != null
            && DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.AfterPublishMessageStore)
        )
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var eventData = new MessageEventDataPubStore
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                Message = message,
                ElapsedTimeMs = now - tracingTimestamp.Value,
            };

            DiagnosticListener.Write(MessageDiagnosticListenerNames.AfterPublishMessageStore, eventData);
        }
    }

    private static void _TracingError(long? tracingTimestamp, Message message, Exception ex)
    {
        if (
            tracingTimestamp != null
            && DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorPublishMessageStore)
        )
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var eventData = new MessageEventDataPubStore
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                Message = message,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                Exception = ex,
            };

            DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorPublishMessageStore, eventData);
        }
    }

    #endregion
}
