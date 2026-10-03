// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.Initialization.Schema;

namespace OpenTelemetry.Trace;

/// <summary>
/// OpenTelemetry tracing registration for the schema runner. Lives in the <c>OpenTelemetry.Trace</c> namespace so it
/// surfaces next to the provider builder without an extra import; only <c>OpenTelemetry.Api</c> surface is used (no
/// SDK dependency).
/// </summary>
[PublicAPI]
public static class HeadlessSchemaRunnerTracingExtensions
{
    /// <summary>
    /// Enables the schema runner's tracing by subscribing to the <see cref="SchemaRunnerDiagnostics.SourceName" />
    /// activity source.
    /// </summary>
    /// <param name="builder">The <see cref="TracerProviderBuilder" /> being configured.</param>
    /// <returns>The same <paramref name="builder" /> for chaining.</returns>
    public static TracerProviderBuilder AddSchemaRunnerInstrumentation(this TracerProviderBuilder builder)
    {
        Argument.IsNotNull(builder);

        return builder.AddSource(SchemaRunnerDiagnostics.SourceName);
    }
}
