// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Reflection;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

/// <summary>
/// Consumer executor
/// </summary>
public interface ISubscribeExecutor
{
    Task<OperateResult> ExecuteAsync(
        MediumMessage message,
        ConsumerExecutorDescriptor? descriptor = null,
        CancellationToken cancellationToken = default
    );
}

internal sealed class SubscribeExecutor : ISubscribeExecutor
{
    // Diagnostics listener
    private static readonly DiagnosticListener _DiagnosticListener = new(
        MessageDiagnosticListenerNames.DiagnosticListenerName
    );

    private readonly string? _hostName;
    private readonly IServiceProvider _provider;
    private readonly IDataStorage _dataStorage;
    private readonly ISubscribeInvoker _invoker;
    private readonly IRetryBackoffStrategy _backoffStrategy;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SubscribeExecutor> _logger;
    private readonly MessagingOptions _options;

    public SubscribeExecutor(
        IServiceProvider provider,
        IDataStorage dataStorage,
        ISubscribeInvoker invoker,
        TimeProvider timeProvider,
        ILogger<SubscribeExecutor> logger,
        IOptions<MessagingOptions> options
    )
    {
        _provider = provider;
        _logger = logger;
        _options = options.Value;

        _dataStorage = dataStorage;
        _invoker = invoker;
        _timeProvider = timeProvider;
        _hostName = Helper.GetInstanceHostname();
        _backoffStrategy = _options.RetryBackoffStrategy;
    }

    public async Task<OperateResult> ExecuteAsync(
        MediumMessage message,
        ConsumerExecutorDescriptor? descriptor = null,
        CancellationToken cancellationToken = default
    )
    {
        if (descriptor == null)
        {
            var selector = _provider.GetRequiredService<MethodMatcherCache>();
            if (!selector.TryGetTopicExecutor(message.Origin.GetName(), message.Origin.GetGroup()!, out descriptor))
            {
                _logger.LogError(
                    "Message (Name:{GetName},Group:{GetGroup}) can not be found subscriber. Ensure the subscriber method is decorated with [Subscribe] and the consumer group matches.",
                    message.Origin.GetName(),
                    message.Origin.GetGroup()
                );

                var exception = new SubscriberNotFoundException(
                    $"Message (Name:{message.Origin.GetName()},Group:{message.Origin.GetGroup()}) can not be found subscriber."
                        + $"{Environment.NewLine} Ensure the subscriber method is decorated with [Subscribe] and the consumer group matches."
                );

                _TracingError(
                    _timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
                    message.Origin,
                    method: null,
                    exception
                );

                await _SetFailedState(message, exception);
                return OperateResult.Failed(exception);
            }
        }

        bool retry;
        OperateResult result;

        //record instance id
        message.Origin.Headers[Headers.ExecutionInstanceId] = _hostName;

        do
        {
            var (shouldRetry, operateResult) = await _ExecuteWithoutRetryAsync(message, descriptor, cancellationToken)
                .ConfigureAwait(false);
            result = operateResult;
            if (result.Equals(OperateResult.Success))
            {
                return result;
            }

            retry = shouldRetry;
        } while (retry);

        return result;
    }

    private async Task<(bool, OperateResult)> _ExecuteWithoutRetryAsync(
        MediumMessage message,
        ConsumerExecutorDescriptor descriptor,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(message);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _logger.ConsumerExecuting(descriptor.ImplTypeInfo.Name, descriptor.MethodInfo.Name, descriptor.GroupName);

            var sp = Stopwatch.StartNew();

            await _InvokeConsumerMethodAsync(message, descriptor, cancellationToken).ConfigureAwait(false);

            sp.Stop();

            await _SetSuccessfulState(message).ConfigureAwait(false);

            MessageEventCounterSource.Log.WriteInvokeTimeMetrics(sp.Elapsed.TotalMilliseconds);
            _logger.ConsumerExecuted(
                descriptor.ImplTypeInfo.Name,
                descriptor.MethodInfo.Name,
                descriptor.GroupName,
                sp.Elapsed.TotalMilliseconds,
                message.Origin.GetExecutionInstanceId()
            );

            return (false, OperateResult.Success);
        }
        catch (Exception ex)
        {
            _logger.ConsumerExecuteFailed(
                message.Origin.GetName(),
                message.DbId,
                message.Origin.GetExecutionInstanceId(),
                ex
            );

            return (await _SetFailedState(message, ex).ConfigureAwait(false), OperateResult.Failed(ex));
        }
    }

    private ValueTask _SetSuccessfulState(MediumMessage message)
    {
        message.ExpiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(_options.SucceedMessageExpiredAfter);

        return _dataStorage.ChangeReceiveStateAsync(message, StatusName.Succeeded);
    }

    private async Task<bool> _SetFailedState(MediumMessage message, Exception ex)
    {
        if (ex is SubscriberNotFoundException)
        {
            message.Retries = _options.FailedRetryCount; // not retry if SubscriberNotFoundException
        }

        var needRetry = _UpdateMessageForRetry(message, ex);

        message.Origin.AddOrUpdateException(ex);
        message.ExpiresAt = message.Added.AddSeconds(_options.FailedMessageExpiredAfter);

        await _dataStorage.ChangeReceiveStateAsync(message, StatusName.Failed).ConfigureAwait(false);

        return needRetry;
    }

    private bool _UpdateMessageForRetry(MediumMessage message, Exception ex)
    {
        // Check if exception is retryable
        if (!_backoffStrategy.ShouldRetry(ex))
        {
            message.Retries = _options.FailedRetryCount; // Mark as exhausted
            _logger.LogWarning(
                "Message {MessageId} failed with non-retryable exception: {ExceptionType}. Skipping retries.",
                message.DbId,
                ex.GetType().Name
            );
            return false;
        }

        var retries = ++message.Retries;

        var retryCount = Math.Min(_options.FailedRetryCount, 3);
        if (retries >= retryCount)
        {
            if (retries == _options.FailedRetryCount)
            {
                try
                {
                    _options.FailedThresholdCallback?.Invoke(
                        new FailedInfo
                        {
                            ServiceProvider = _provider,
                            MessageType = MessageType.Subscribe,
                            Message = message.Origin,
                        }
                    );

                    _logger.ConsumerExecutedAfterThreshold(message.DbId, _options.FailedRetryCount);
                }
                catch (Exception callbackEx)
                {
                    _logger.ExecutedThresholdCallbackFailed(callbackEx);
                }
            }

            return false;
        }

        _logger.ConsumerExecutionRetrying(message.DbId, retries);

        return true;
    }

    private async Task _InvokeConsumerMethodAsync(
        MediumMessage message,
        ConsumerExecutorDescriptor descriptor,
        CancellationToken cancellationToken
    )
    {
        var consumerContext = new ConsumerContext(descriptor, message);
        var tracingTimestamp = _TracingBefore(message.Origin, descriptor.MethodInfo);
        try
        {
            var ret = await _invoker.InvokeAsync(consumerContext, cancellationToken).ConfigureAwait(false);

            _TracingAfter(tracingTimestamp, message.Origin, descriptor.MethodInfo);

            if (!string.IsNullOrEmpty(ret.CallbackName))
            {
                ret.CallbackHeader ??= new Dictionary<string, string?>(StringComparer.Ordinal);
                ret.CallbackHeader[Headers.CorrelationId] = message.Origin.GetId();
                ret.CallbackHeader[Headers.CorrelationSequence] = (
                    message.Origin.GetCorrelationSequence() + 1
                ).ToString(CultureInfo.InvariantCulture);

                if (message.Origin.Headers.TryGetValue(Headers.TraceParent, out var traceParent))
                {
                    ret.CallbackHeader[Headers.TraceParent] = traceParent;
                }

                await _provider
                    .GetRequiredService<IOutboxPublisher>()
                    .PublishAsync(ret.CallbackName, ret.Result, ret.CallbackHeader, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            //ignore
        }
        catch (Exception ex)
        {
            var e = new SubscriberExecutionFailedException(ex.Message, ex);

            _TracingError(tracingTimestamp, message.Origin, descriptor.MethodInfo, e);

            e.ReThrow();
        }
    }

    #region tracing

    private static long? _TracingBefore(Message message, MethodInfo method)
    {
        if (_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.BeforeSubscriberInvoke))
        {
            var eventData = new MessageEventDataSubExecute
            {
                OperationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Operation = message.GetName(),
                Message = message,
                MethodInfo = method,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.BeforeSubscriberInvoke, eventData);

            return eventData.OperationTimestamp;
        }

        return null;
    }

    private static void _TracingAfter(long? tracingTimestamp, Message message, MethodInfo method)
    {
        MessageEventCounterSource.Log.WriteInvokeMetrics();
        if (
            tracingTimestamp != null
            && _DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.AfterSubscriberInvoke)
        )
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var eventData = new MessageEventDataSubExecute
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                Message = message,
                MethodInfo = method,
                ElapsedTimeMs = now - tracingTimestamp.Value,
            };

            _DiagnosticListener.Write(MessageDiagnosticListenerNames.AfterSubscriberInvoke, eventData);
        }
    }

    private void _TracingError(long? tracingTimestamp, Message message, MethodInfo? method, Exception ex)
    {
        if (!_DiagnosticListener.IsEnabled(MessageDiagnosticListenerNames.ErrorSubscriberInvoke))
        {
            return;
        }

        var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

        var eventData = new MessageEventDataSubExecute
        {
            OperationTimestamp = now,
            Operation = message.GetName(),
            Message = message,
            MethodInfo = method,
            ElapsedTimeMs = now - tracingTimestamp,
            Exception = ex,
        };

        _DiagnosticListener.Write(MessageDiagnosticListenerNames.ErrorSubscriberInvoke, eventData);
    }

    #endregion
}
