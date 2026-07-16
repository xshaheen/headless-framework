// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using OpenTelemetry.Context.Propagation;

namespace Headless.Messaging.Internal;

/// <summary>
/// Async-scoped holder for the trace context extracted at consume time. When a subscriber handler publishes an
/// outgoing message while messaging telemetry is enabled but no <see cref="Activity"/> was started (metrics-only
/// config, or the sampler dropped it), <see cref="MessagingTelemetry.PublishStart"/> reads this to relay the
/// incoming <c>traceparent</c>/baggage verbatim (K4 pass-through), preserving trace continuity through a
/// non-tracing service instead of silently starting a fresh root downstream.
/// </summary>
/// <remarks>
/// Written by <see cref="MessagingTelemetry.ConsumeStart"/> / <see cref="MessagingTelemetry.SubscriberInvokeStart"/>,
/// and only when the header extraction yielded a non-default context (those sites already run solely under the
/// <c>MessagingDiagnostics.IsEnabled</c> caller gate, so a fully-unobserved host never touches this). The value
/// flows <em>down</em> into the subscriber handler via <see cref="AsyncLocal{T}"/>; it is intentionally never
/// restored or cleared — callee-side writes do not flow back to the dispatch loop, so there is no cross-message
/// leakage.
/// </remarks>
internal static class MessagingAmbientContext
{
    private static readonly AsyncLocal<PropagationContext> _Current = new();

    /// <summary>The consume-scope propagation context flowing into the current async scope, or default.</summary>
    internal static PropagationContext Current
    {
        get => _Current.Value;
        set => _Current.Value = value;
    }
}
