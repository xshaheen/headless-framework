// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Messages;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace OpenTelemetry.Trace;

public static class MessagingOpenTelemetrySetup
{
    /// <summary>
    /// Enables the message eventing data collection for CAP.
    /// </summary>
    /// <param name="builder"><see cref="TracerProviderBuilder" /> being configured.</param>
    /// <returns>The instance of <see cref="TracerProviderBuilder" /> to chain the calls.</returns>
    public static TracerProviderBuilder AddMessagingInstrumentation(this TracerProviderBuilder builder)
    {
        Argument.IsNotNull(builder);

        builder.AddSource(DiagnosticListener.SourceName);

        var instrumentation = new MessagingInstrumentation(new DiagnosticListener());

        return builder.AddInstrumentation(() => instrumentation);
    }
}
