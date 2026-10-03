// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Messaging.Configuration;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Messages;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Internal;

/// <summary>
/// The receive stage both deliveries share: the receive-middleware ring, contract-version validation, deserialization,
/// the poison-on-arrival envelope, and consume tracing.
/// </summary>
internal sealed partial class ConsumerRegister
{
    private static void _ValidateMessageContractVersion(
        IDictionary<string, string?> headers,
        ConsumerExecutorDescriptor executor
    )
    {
        var receivedVersion = headers.TryGetValue(Headers.ContractVersion, out var value)
            ? MessagingOptions.ValidateContractVersion(value ?? string.Empty)
            : MessageOptions.InitialContractVersion;

        headers[Headers.ContractVersion] = receivedVersion;

        if (
            !string.IsNullOrWhiteSpace(executor.MessageContractVersion)
            && !string.Equals(executor.MessageContractVersion, receivedVersion, StringComparison.Ordinal)
        )
        {
            throw new InvalidOperationException(
                $"Message contract version '{receivedVersion}' does not match registered message contract version "
                    + $"'{executor.MessageContractVersion}' for '{executor.MessageName}'."
            );
        }
    }

    /// <summary>
    /// The inner receive steps the ring's innermost <c>next</c> runs: contract-version validation,
    /// Stage A deserialization (wrapped in <see cref="MessageDeserializationException"/>), and the
    /// null-payload check for typed consumers.
    /// </summary>
    private async ValueTask<Message> _RunInnerReceiveAsync(
        IDictionary<string, string?> currentHeaders,
        ReadOnlyMemory<byte> currentBody,
        ConsumerExecutorDescriptor executor,
        CancellationToken cancellationToken
    )
    {
        _ValidateMessageContractVersion(currentHeaders, executor);

        var effectiveTransport = new TransportMessage(currentHeaders, currentBody);
        Message deserialized;

        // Only the deserialize call is wrapped: a serializer failure is a terminal payload defect,
        // while a version mismatch keeps its InvalidOperationException so middleware can catch it
        // and convert it into a Reject decision before it reaches the poison store.
        try
        {
            deserialized = await _serializer
                .DeserializeAsync(effectiveTransport, executor.MessageType, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception deserializeEx)
        {
            throw new MessageDeserializationException(
                $"Failed to deserialize the message body for '{executor.MessageName}' into "
                    + $"'{executor.MessageType}' (subscription '{executor.SubscriptionName}'): {deserializeEx.Message}",
                deserializeEx
            );
        }

        // An empty body for a typed consumer is the same deterministic payload defect as a malformed
        // one; untyped consumers (null MessageType) keep Value = null passing as before.
        if (executor.MessageType is { } typedPayload && deserialized.Value is null)
        {
            throw new MessageDeserializationException(MessageDeserializationException.EmptyBody(typedPayload));
        }

        deserialized.RemoveException();

        return deserialized;
    }

    /// <summary>
    /// Runs the receive stage for one delivery: the receive-middleware ring when descriptors match
    /// this consumer, otherwise the inner steps directly (the pre-middleware fast path — no scope,
    /// no context, no allocation beyond the outcome record).
    /// </summary>
    private async ValueTask<ReceiveRingOutcome> _RunReceiveRingAsync(
        TransportMessage transportMessage,
        ConsumerExecutorDescriptor executor,
        MessageLane lane,
        Func<
            IDictionary<string, string?>,
            ReadOnlyMemory<byte>,
            ConsumerExecutorDescriptor,
            CancellationToken,
            ValueTask<Message>
        > innerReceive,
        CancellationToken hostShutdownToken
    )
    {
        // Runtime subscriptions can carry a null payload type (no typed handler parameter). They
        // still flow through the ring: object stands in as the context/lookup type, so global
        // receive middleware runs and only descriptors explicitly registered for object match.
        var messageType = executor.MessageType ?? typeof(object);
        IReadOnlyList<MiddlewareDescriptor>? descriptors = null;
        var hasDescriptors =
            _middlewareDescriptorRegistry is not null
            && _middlewareDescriptorRegistry.TryGetReceiveDescriptors(messageType, lane, out descriptors);

        if (!hasDescriptors)
        {
            // Zero-middleware fast path: identical to the pre-receive-ring behavior. The header
            // dictionary and body are the transport's own, exactly as before.
            return new ReceiveRingOutcome(
                ReceiveRingResult.Accepted,
                Message: await innerReceive(
                        transportMessage.Headers,
                        transportMessage.Body,
                        executor,
                        hostShutdownToken
                    )
                    .ConfigureAwait(false)
            );
        }

        var context = new ReceiveContext(
            transportMessage.Id,
            transportMessage.Name,
            executor.ResolvedConsumerIdentity,
            lane,
            messageType,
            executor.MessageContractVersion,
            transportMessage.Headers,
            transportMessage.Body,
            hostShutdownToken
        );

        // Hoisted so every middleware's error filter captures one loop-invariant reference, and so
        // the accept path can detect a post-success throw from any ring member.
        var innerCompleted = new StrongBox<bool>(value: false);
        Message? innerMessage = null;

        Func<ValueTask> next = async () =>
        {
            // Single-shot: a second invocation would re-run version stamping and deserialization on
            // a context that has already completed.
            if (innerCompleted.Value)
            {
                throw new InvalidOperationException(
                    "The receive pipeline's next delegate can only be invoked once per delivery."
                );
            }

            // The inner steps always consume the context's CURRENT view: middleware transformations
            // (SetHeader/RemoveHeader/ReplaceBody) apply before the framework reads the envelope. The
            // received dictionary is never handed to the inner steps once a copy-on-write view exists.
            var currentHeaders = ReferenceEquals(context.Headers, transportMessage.Headers)
                ? transportMessage.Headers
                : _AsWritableHeaders(context.Headers);

            innerMessage = await innerReceive(currentHeaders, context.Body, executor, context.CancellationToken)
                .ConfigureAwait(false);

            context.MarkCompleted();
            innerCompleted.Value = true;
        };

        async ValueTask InvokeMiddleware(IReceiveMiddleware middleware, Func<ValueTask> innerNext)
        {
            var middlewareType = middleware.GetType().FullName ?? middleware.GetType().Name;

            try
            {
                await middleware.InvokeAsync(context, innerNext).ConfigureAwait(false);
                context.MarkCompleted();
            }
            catch (Exception ex) when (innerCompleted.Value)
            {
                // The inner pipeline already produced its message; suppressing the fault keeps the
                // accepted delivery instead of discarding it after its work has been done.
                _logger.ReceivePostSuccessMiddlewareFailed(ex, middlewareType);
                return;
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == context.CancellationToken)
            {
                throw new OperationCanceledException(ex.Message, ex, context.CancellationToken);
            }

            // A swapped-in token (SetCancellationToken) that became cancelled between middleware
            // returns is a cooperative stop, not a fault.
            context.CancellationToken.ThrowIfCancellationRequested();
        }

        // Per-delivery scope: middleware resolves from a fresh scope so scoped registrations see
        // per-delivery state; the ring (and the scope) complete before any storage write or dispatch.
        await using var scope = serviceScopeFactory.CreateAsyncScope();

        IReceiveMiddleware? outermost = null;
        foreach (var descriptor in descriptors!)
        {
            var middleware = scope
                .ServiceProvider.GetServices(descriptor.ServiceType)
                .FirstOrDefault(service => service?.GetType() == descriptor.MiddlewareType);
            if (middleware is not IReceiveMiddleware matched)
            {
                continue;
            }

            outermost ??= matched;
            var current = matched;
            var innerNext = next;
            next = () => InvokeMiddleware(current, innerNext);
        }

        if (outermost is null)
        {
            // Descriptors matched but every resolution came back empty (a registered service replaced
            // by a different implementation): degrade to the direct inner run rather than dropping
            // the delivery.
            return new ReceiveRingOutcome(
                ReceiveRingResult.Accepted,
                Message: await innerReceive(
                        transportMessage.Headers,
                        transportMessage.Body,
                        executor,
                        hostShutdownToken
                    )
                    .ConfigureAwait(false)
            );
        }

        var outermostType = outermost.GetType();

        try
        {
            await next().ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == context.CancellationToken)
        {
            return new ReceiveRingOutcome(
                ReceiveRingResult.Cancelled,
                Exception: ex,
                Context: context,
                MiddlewareType: outermostType.FullName ?? outermostType.Name
            );
        }
        catch (Exception ex)
        {
            // Faults thrown from middleware code (or the inner steps when middleware let them
            // escape) map to the Reject row; cancellation bound to the context token was handled above.
            return new ReceiveRingOutcome(
                ReceiveRingResult.Rejected,
                Exception: ex,
                Context: context,
                MiddlewareType: outermostType.FullName ?? outermostType.Name,
                OutcomeReason: context.Outcome is ReceiveOutcome.Reject ? context.OutcomeReason : null,
                IsPolicyReject: false
            );
        }

        if (innerCompleted.Value)
        {
            if (context.Outcome is ReceiveOutcome.Skip or ReceiveOutcome.Reject)
            {
                // Declaring an outcome after a completed next is contradictory — the delivery was
                // already accepted. Treat it as the undeclared-outcome fault it is.
                return new ReceiveRingOutcome(
                    ReceiveRingResult.Rejected,
                    Exception: new ReceiveOutcomeUndeclaredException(outermostType),
                    Context: context,
                    MiddlewareType: outermostType.FullName ?? outermostType.Name
                );
            }

            return new ReceiveRingOutcome(
                ReceiveRingResult.Accepted,
                Message: innerMessage,
                Context: context,
                MiddlewareType: outermostType.FullName ?? outermostType.Name
            );
        }

        // The ring returned without reaching the inner steps.
        if (context.Outcome == ReceiveOutcome.Skip)
        {
            return new ReceiveRingOutcome(
                ReceiveRingResult.Skipped,
                Context: context,
                MiddlewareType: outermostType.FullName ?? outermostType.Name,
                OutcomeReason: context.OutcomeReason
            );
        }

        var isPolicyReject = context.Outcome == ReceiveOutcome.Reject;
        var rejectException = isPolicyReject
            ? context.RejectCause ?? new ReceiveMessageRejectedException(context.OutcomeReason!)
            : new ReceiveOutcomeUndeclaredException(outermostType);

        return new ReceiveRingOutcome(
            ReceiveRingResult.Rejected,
            Exception: rejectException,
            Context: context,
            MiddlewareType: outermostType.FullName ?? outermostType.Name,
            OutcomeReason: context.OutcomeReason,
            IsPolicyReject: isPolicyReject
        );
    }

    /// <summary>
    /// Builds the poison-on-arrival envelope. The <c>data:</c> URI always carries the RECEIVED bytes
    /// (capped); the headers are the CURRENT view when receive middleware transformed the envelope
    /// (the framework's Exception stamp is allowed on that view), else the received headers.
    /// </summary>
    private Message _BuildPoisonMessage(TransportMessage transportMessage, ReceiveContext? context, Exception exception)
    {
        var headers =
            context is not null && !ReferenceEquals(context.Headers, transportMessage.Headers)
                ? _AsWritableHeaders(context.Headers)
                : transportMessage.Headers;

        headers[Headers.Exception] = exception.GetType().Name;

        return new Message(headers, _BuildCappedDataUri(transportMessage.Body, headers));
    }

    /// <summary>
    /// Builds the capped <c>data:</c> URI from the received body: whole at/below
    /// <see cref="MessagingOptions.MaxPoisonEnvelopeBytes"/>, a <c>truncated</c>-marked prefix past
    /// it, and omitted entirely beyond four times the cap (headers-only poison row). Measured on the
    /// raw bytes, before base64 encoding.
    /// </summary>
    private string? _BuildCappedDataUri(ReadOnlyMemory<byte> receivedBody, IDictionary<string, string?> headers)
    {
        if (receivedBody.Length == 0)
        {
            return null;
        }

        var mediaType =
            headers.TryGetValue(Headers.Type, out var type) && !string.IsNullOrWhiteSpace(type) ? type : "UnknownType";

        if (_options.MaxPoisonEnvelopeBytes is not { } limit)
        {
            return $"data:{mediaType};base64," + receivedBody.Span.ToBase64();
        }

        if (receivedBody.Length > 4L * limit)
        {
            // Far past the cap: even a truncated prefix would dwarf the rest of the row.
            return null;
        }

        if (receivedBody.Length <= limit)
        {
            return $"data:{mediaType};base64," + receivedBody.Span.ToBase64();
        }

        return $"data:{mediaType};truncated;base64," + receivedBody.Span[..limit].ToBase64();
    }

    private static Dictionary<string, string?> _AsWritableHeaders(IReadOnlyDictionary<string, string?> headers)
    {
        return new Dictionary<string, string?>(headers, StringComparer.Ordinal);
    }

    private enum ReceiveRingResult
    {
        Accepted = 0,
        Skipped = 1,
        Rejected = 2,
        Cancelled = 3,
    }

    private sealed record ReceiveRingOutcome(
        ReceiveRingResult Result,
        Message? Message = null,
        Exception? Exception = null,
        ReceiveContext? Context = null,
        string? MiddlewareType = null,
        string? OutcomeReason = null,
        bool IsPolicyReject = false
    );

    #region Tracing

    private MessagingTraceHandle _TracingBefore(TransportMessage message, MessageLane lane, BrokerAddress broker)
    {
        if (!MessagingDiagnostics.IsEnabled)
        {
            return default;
        }

        var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var activity = _telemetry.ConsumeStart(message, lane, broker, now);

        return new MessagingTraceHandle(activity, now);
    }

    private void _TracingAfter(MessagingTraceHandle traceHandle, TransportMessage message, BrokerAddress broker)
    {
        MessageEventCounterSource.Log.WriteConsumeMetrics();

        if (!traceHandle.IsRecording)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        MessagingTelemetry.ConsumeStop(traceHandle.Activity, message, broker, traceHandle.StartTimestampMs!.Value, now);
    }

    private static void _TracingError(
        MessagingTraceHandle traceHandle,
        TransportMessage message,
        BrokerAddress broker,
        Exception ex
    )
    {
        if (!traceHandle.IsRecording)
        {
            return;
        }

        MessagingTelemetry.ConsumeError(traceHandle.Activity, message, broker, ex);
    }

    #endregion
}
