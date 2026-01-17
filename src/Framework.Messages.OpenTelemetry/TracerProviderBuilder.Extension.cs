// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNetCore.CAP.OpenTelemetry;
using Framework.Checks;

// ReSharper disable once CheckNamespace
namespace OpenTelemetry.Trace;

public static class TracerProviderBuilderExtensions
{
    /// <summary>
    /// Enables the message eventing data collection for CAP.
    /// </summary>
    /// <param name="builder"><see cref="TracerProviderBuilder" /> being configured.</param>
    /// <returns>The instance of <see cref="TracerProviderBuilder" /> to chain the calls.</returns>
    public static TracerProviderBuilder AddCapInstrumentation(this TracerProviderBuilder builder)
    {
        Argument.IsNotNull(builder);

        builder.AddSource(DiagnosticListener.SourceName);

        var instrumentation = new CapInstrumentation(new DiagnosticListener());

        return builder.AddInstrumentation(() => instrumentation);
    }
}
