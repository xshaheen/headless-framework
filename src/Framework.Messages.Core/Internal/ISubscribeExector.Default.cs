// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Reflection;
using Framework.Checks;
using Framework.Messages.Configuration;
using Framework.Messages.Diagnostics;
using Framework.Messages.Exceptions;
using Framework.Messages.Messages;
using Framework.Messages.Monitoring;
using Framework.Messages.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Messages.Internal;

internal class SubscribeExecutor : ISubscribeExecutor
{
    // diagnostics listener
    // ReSharper disable once InconsistentNaming
    private static readonly DiagnosticListener s_diagnosticListener = new(
        CapDiagnosticListenerNames.DiagnosticListenerName
    );

    private readonly IDataStorage _dataStorage;
    private readonly string? _hostName;
    private readonly ILogger _logger;
    private readonly CapOptions _options;
    private readonly IServiceProvider _provider;
    private readonly TimeProvider _timeProvider;

    public SubscribeExecutor(ILogger<SubscribeExecutor> logger, IOptions<CapOptions> options, IServiceProvider provider)
    {
        _provider = provider;
        _logger = logger;
        _options = options.Value;

        _dataStorage = _provider.GetRequiredService<IDataStorage>();
        Invoker = _provider.GetRequiredService<ISubscribeInvoker>();
        _timeProvider = _provider.GetRequiredService<TimeProvider>();
        _hostName = Helper.GetInstanceHostname();
    }

    private ISubscribeInvoker Invoker { get; }

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
                var error =
                    $"Message (Name:{message.Origin.GetName()},Group:{message.Origin.GetGroup()}) can not be found subscriber."
                    + $"{Environment.NewLine} see: https://github.com/dotnetcore/CAP/issues/63";
                _logger.LogError(error);

                _TracingError(
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    message.Origin,
                    null,
                    new Exception(error)
                );

                var ex = new SubscriberNotFoundException(error);
                await _SetFailedState(message, ex);
                return OperateResult.Failed(ex);
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

            CapEventCounterSource.Log.WriteInvokeTimeMetrics(sp.Elapsed.TotalMilliseconds);
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

    private Task _SetSuccessfulState(MediumMessage message)
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

        var needRetry = _UpdateMessageForRetry(message);

        message.Origin.AddOrUpdateException(ex);
        message.ExpiresAt = message.Added.AddSeconds(_options.FailedMessageExpiredAfter);

        await _dataStorage.ChangeReceiveStateAsync(message, StatusName.Failed).ConfigureAwait(false);

        return needRetry;
    }

    private bool _UpdateMessageForRetry(MediumMessage message)
    {
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
                catch (Exception ex)
                {
                    _logger.ExecutedThresholdCallbackFailed(ex);
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
            var ret = await Invoker.InvokeAsync(consumerContext, cancellationToken).ConfigureAwait(false);

            _TracingAfter(tracingTimestamp, message.Origin, descriptor.MethodInfo);

            if (!string.IsNullOrEmpty(ret.CallbackName))
            {
                ret.CallbackHeader ??= new Dictionary<string, string?>();
                ret.CallbackHeader[Headers.CorrelationId] = message.Origin.GetId();
                ret.CallbackHeader[Headers.CorrelationSequence] = (
                    message.Origin.GetCorrelationSequence() + 1
                ).ToString();

                if (message.Origin.Headers.TryGetValue(Headers.TraceParent, out var traceparent))
                {
                    ret.CallbackHeader[Headers.TraceParent] = traceparent;
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

    private long? _TracingBefore(Message message, MethodInfo method)
    {
        if (s_diagnosticListener.IsEnabled(CapDiagnosticListenerNames.BeforeSubscriberInvoke))
        {
            var eventData = new CapEventDataSubExecute
            {
                OperationTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Operation = message.GetName(),
                Message = message,
                MethodInfo = method,
            };

            s_diagnosticListener.Write(CapDiagnosticListenerNames.BeforeSubscriberInvoke, eventData);

            return eventData.OperationTimestamp;
        }

        return null;
    }

    private void _TracingAfter(long? tracingTimestamp, Message message, MethodInfo method)
    {
        CapEventCounterSource.Log.WriteInvokeMetrics();
        if (
            tracingTimestamp != null
            && s_diagnosticListener.IsEnabled(CapDiagnosticListenerNames.AfterSubscriberInvoke)
        )
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var eventData = new CapEventDataSubExecute
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                Message = message,
                MethodInfo = method,
                ElapsedTimeMs = now - tracingTimestamp.Value,
            };

            s_diagnosticListener.Write(CapDiagnosticListenerNames.AfterSubscriberInvoke, eventData);
        }
    }

    private void _TracingError(long? tracingTimestamp, Message message, MethodInfo? method, Exception ex)
    {
        if (
            tracingTimestamp != null
            && s_diagnosticListener.IsEnabled(CapDiagnosticListenerNames.ErrorSubscriberInvoke)
        )
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var eventData = new CapEventDataSubExecute
            {
                OperationTimestamp = now,
                Operation = message.GetName(),
                Message = message,
                MethodInfo = method,
                ElapsedTimeMs = now - tracingTimestamp.Value,
                Exception = ex,
            };

            s_diagnosticListener.Write(CapDiagnosticListenerNames.ErrorSubscriberInvoke, eventData);
        }
    }

    #endregion
}
