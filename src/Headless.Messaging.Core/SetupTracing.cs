// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace OpenTelemetry.Trace;

/// <summary>
/// OpenTelemetry tracing registration for the messaging subsystem. Lives in the <c>OpenTelemetry.Trace</c>
/// namespace so it surfaces next to the provider builder without an extra <c>using Headless.Messaging</c> import;
/// only <c>OpenTelemetry.Api</c> surface is used (no SDK dependency).
/// </summary>
[PublicAPI]
public static class HeadlessMessagingTracingExtensions
{
    /// <summary>
    /// Enables messaging tracing by subscribing to the <see cref="MessagingDiagnostics.SourceName"/> activity source.
    /// </summary>
    /// <param name="builder">The <see cref="TracerProviderBuilder"/> being configured.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static TracerProviderBuilder AddMessagingInstrumentation(this TracerProviderBuilder builder)
    {
        Argument.IsNotNull(builder);

        return builder.AddSource(MessagingDiagnostics.SourceName);
    }
}
