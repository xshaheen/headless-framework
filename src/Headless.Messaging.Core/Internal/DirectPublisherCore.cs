// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Diagnostics;
using Headless.Messaging.Messages;
using Headless.Messaging.Serialization;

namespace Headless.Messaging.Internal;

/// <summary>
/// Shared send-and-trace kernel for direct publishers (<see cref="Bus"/> and <see cref="Queue"/>).
/// Handles serialization, transport send, and native <c>message.publish</c> span + metric emission so neither
/// publisher duplicates the pattern.
/// </summary>
internal static class DirectPublisherCore
{
    internal static async Task SendAsync(
        Message message,
        MessageLane lane,
        ISerializer serializer,
        BrokerAddress brokerAddress,
        Func<TransportMessage, CancellationToken, Task<OperateResult>> sendTransport,
        Func<long> nowMs,
        MessagingTelemetry telemetry,
        TimeSpan transportPublishTimeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken
    )
    {
        var transportMsg = await serializer
            .SerializeToTransportMessageAsync(message, cancellationToken)
            .ConfigureAwait(false);

        using var timeoutCts = new CancellationTokenSource(transportPublishTimeout, timeProvider);
        using var publishCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var traceHandle = _TracingBeforeSend(transportMsg, lane, brokerAddress, nowMs, telemetry);
        try
        {
            var result = await sendTransport(transportMsg, publishCts.Token).ConfigureAwait(false);

            if (!result.Succeeded)
            {
                var ex = new PublisherSentFailedException(result.ToString(), result.Exception);
                _TracingErrorSend(traceHandle, transportMsg, lane, brokerAddress, ex);
                throw ex;
            }

            _TracingAfterSend(traceHandle, transportMsg, lane, brokerAddress, nowMs);
        }
        catch (OperationCanceledException ex)
        {
            // Cancellation can race transport acceptance. Diagnose the outcome as ambiguous while
            // preserving the caller's original cancellation exception.
            MessagingTelemetry.PublishAmbiguous(traceHandle.Activity, transportMsg, brokerAddress, ex, lane);

            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (Exception e) when (e is not PublisherSentFailedException)
        {
            try
            {
                _TracingErrorSend(traceHandle, transportMsg, lane, brokerAddress, e);
            }
#pragma warning disable ERP022 // Intentional: tracing failure should not mask the original exception
            catch
            {
                // Tracing failure should not mask the original exception.
            }
#pragma warning restore ERP022

            throw;
        }
    }

    private static MessagingTraceHandle _TracingBeforeSend(
        TransportMessage message,
        MessageLane lane,
        BrokerAddress brokerAddress,
        Func<long> nowMs,
        MessagingTelemetry telemetry
    )
    {
        MessageEventCounterSource.Log.WritePublishMetrics();

        if (!MessagingDiagnostics.IsEnabled)
        {
            return default;
        }

        var now = nowMs();
        var activity = telemetry.PublishStart(message, lane, brokerAddress, now);

        return new MessagingTraceHandle(activity, now);
    }

    private static void _TracingAfterSend(
        MessagingTraceHandle traceHandle,
        TransportMessage message,
        MessageLane lane,
        BrokerAddress brokerAddress,
        Func<long> nowMs
    )
    {
        if (!traceHandle.IsRecording)
        {
            return;
        }

        MessagingTelemetry.PublishStop(
            traceHandle.Activity,
            message,
            brokerAddress,
            traceHandle.StartTimestampMs!.Value,
            nowMs(),
            lane
        );
    }

    private static void _TracingErrorSend(
        MessagingTraceHandle traceHandle,
        TransportMessage message,
        MessageLane lane,
        BrokerAddress brokerAddress,
        Exception exception
    )
    {
        if (!traceHandle.IsRecording)
        {
            return;
        }

        MessagingTelemetry.PublishError(traceHandle.Activity, message, brokerAddress, exception, lane);
    }
}
