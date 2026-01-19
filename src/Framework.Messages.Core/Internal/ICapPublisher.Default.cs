// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Framework.Abstractions;
using Framework.Checks;
using Framework.Messages.Configuration;
using Framework.Messages.Diagnostics;
using Framework.Messages.Messages;
using Framework.Messages.Persistence;
using Framework.Messages.Transactions;
using Framework.Messages.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Framework.Messages.Internal;

internal class OutboxPublisher(IServiceProvider service) : IOutboxPublisher
{
    // ReSharper disable once InconsistentNaming
    protected static DiagnosticListener DiagnosticListener { get; } =
        new(CapDiagnosticListenerNames.DiagnosticListenerName);

    private readonly CapOptions _capOptions = service.GetRequiredService<IOptions<CapOptions>>().Value;
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
        await _PublishInternalAsync(name, value, headers, null, cancellationToken).ConfigureAwait(false);
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

        await PublishAsync(name, value, headers, cancellationToken).ConfigureAwait(false);
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

        await _PublishInternalAsync(name, value, headers, delayTime, cancellationToken).ConfigureAwait(false);
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

        await PublishDelayAsync(delayTime, name, value, header, cancellationToken).ConfigureAwait(false);
    }

    public void Publish<T>(string name, T? value, string? callbackName = null)
    {
        PublishAsync(name, value, callbackName).ConfigureAwait(false).GetAwaiter().GetResult();
    }

    public void Publish<T>(string name, T? value, IDictionary<string, string?> headers)
    {
        PublishAsync(name, value, headers).ConfigureAwait(false).GetAwaiter().GetResult();
    }

    public void PublishDelay<T>(TimeSpan delayTime, string name, T? value, IDictionary<string, string?> headers)
    {
        PublishDelayAsync(delayTime, name, value, headers).ConfigureAwait(false).GetAwaiter().GetResult();
    }

    public void PublishDelay<T>(TimeSpan delayTime, string name, T? value, string? callbackName = null)
    {
        PublishDelayAsync(delayTime, name, value, callbackName).ConfigureAwait(false).GetAwaiter().GetResult();
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

        if (!string.IsNullOrEmpty(_capOptions.TopicNamePrefix))
        {
            name = $"{_capOptions.TopicNamePrefix}.{name}";
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
                var mediumMessage = await _storage.StoreMessageAsync(name, message).ConfigureAwait(false);

                _TracingAfter(tracingTimestamp, message);

                if (delayTime != null)
                {
                    await _dispatcher.EnqueueToScheduler(mediumMessage, publishTime).ConfigureAwait(false);
                }
                else
                {
                    await _dispatcher.EnqueueToPublish(mediumMessage).ConfigureAwait(false);
                }
            }
            else
            {
                var transaction = (OutboxTransactionBase)Transaction;

                var mediumMessage = await _storage
                    .StoreMessageAsync(name, message, transaction.DbTransaction)
                    .ConfigureAwait(false);

                _TracingAfter(tracingTimestamp, message);

                transaction.AddToSent(mediumMessage);

                if (transaction.AutoCommit)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
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
        if (DiagnosticListener.IsEnabled(CapDiagnosticListenerNames.BeforePublishMessageStore))
        {
            var eventData = new CapEventDataPubStore
            {
                OperationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Operation = message.GetName(),
                Message = message,
            };

            DiagnosticListener.Write(CapDiagnosticListenerNames.BeforePublishMessageStore, eventData);

            return eventData.OperationTimestamp;
        }

        return null;
    }

    private static void _TracingAfter(long? tracingTimestamp, Message message)
    {
        if (
            tracingTimestamp != null
            && DiagnosticListener.IsEnabled(CapDiagnosticListenerNames.AfterPublishMessageStore)
        )
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var eventData = new CapEventDataPubStore
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                Message = message,
                ElapsedTimeMs = now - tracingTimestamp.Value,
            };

            DiagnosticListener.Write(CapDiagnosticListenerNames.AfterPublishMessageStore, eventData);
        }
    }

    private static void _TracingError(long? tracingTimestamp, Message message, Exception ex)
    {
        if (
            tracingTimestamp != null
            && DiagnosticListener.IsEnabled(CapDiagnosticListenerNames.ErrorPublishMessageStore)
        )
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var eventData = new CapEventDataPubStore
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                Message = message,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                Exception = ex,
            };

            DiagnosticListener.Write(CapDiagnosticListenerNames.ErrorPublishMessageStore, eventData);
        }
    }

    #endregion
}
