// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Constants;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

/// <summary>
/// Centralises the <see cref="ActivitySource"/> and <see cref="Meter"/> instances used by the messaging
/// subsystem. Every emission site (publish, persist, consume, subscriber-invoke) shares these singletons so
/// traces and metrics land in a single named scope (<c>Headless.Messaging</c>). Consumers subscribe via
/// <see cref="SourceName"/> (<c>AddSource</c>/<c>AddMeter</c>) or the typed <c>AddMessagingInstrumentation()</c>
/// extensions on the OpenTelemetry provider builders.
/// </summary>
/// <remarks>
/// Instrument and standard-dimension names follow the OpenTelemetry messaging semantic conventions
/// (<c>messaging.publish.messages</c>, <c>messaging.consume.duration</c>, …); framework-specific span attributes
/// use the bespoke <c>headless.messaging.*</c> namespace (see <see cref="MessagingTags"/>). Only the
/// Meter/ActivitySource itself carries the framework name <c>Headless.Messaging</c>.
/// </remarks>
[PublicAPI]
public static class MessagingDiagnostics
{
    /// <summary>The full activity-source / meter name used by the messaging subsystem (<c>Headless.Messaging</c>).</summary>
    public const string SourceName = HeadlessDiagnostics.Prefix + "Messaging";

    /// <summary>Shared <see cref="ActivitySource"/> for messaging traces.</summary>
    internal static readonly ActivitySource ActivitySource = HeadlessDiagnostics.CreateActivitySource("Messaging");

    /// <summary>Shared <see cref="Meter"/> for messaging metrics.</summary>
    internal static readonly Meter Meter = HeadlessDiagnostics.CreateMeter("Messaging");

    /// <summary>
    /// Gets whether any span or metric listener is attached to the messaging scope. Emission sites gate their
    /// fast path on this so an unobserved messaging pipeline pays no instrumentation cost (no timestamp capture,
    /// no span creation, no enrichment).
    /// </summary>
    internal static bool IsEnabled => ActivitySource.HasListeners() || MessagingMetrics.AnyEnabled;

    /// <summary>
    /// Starts a new <see cref="Activity"/> with the given <paramref name="name"/> and
    /// <paramref name="parentContext"/> if a listener is attached; otherwise returns <see langword="null"/>.
    /// </summary>
    /// <param name="name">The activity operation name (for example <c>message.publish</c>).</param>
    /// <param name="kind">The activity kind.</param>
    /// <param name="parentContext">The parent trace context, or <see langword="default"/> for a root span.</param>
    /// <returns>The started activity, or <see langword="null"/> when no listener is subscribed.</returns>
    internal static Activity? Start(string name, ActivityKind kind, ActivityContext parentContext)
    {
        return ActivitySource.StartActivity(name, kind, parentContext);
    }
}
