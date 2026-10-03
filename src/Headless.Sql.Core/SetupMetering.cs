// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Sql;

namespace OpenTelemetry.Metrics;

/// <summary>
/// OpenTelemetry metrics registration for the SQL store kit. Lives in the <c>OpenTelemetry.Metrics</c> namespace so it
/// surfaces next to the provider builder without an extra <c>using Headless.Sql</c> import; only
/// <c>OpenTelemetry.Api</c> surface is used (no SDK dependency).
/// </summary>
[PublicAPI]
public static class HeadlessSqlMeteringExtensions
{
    /// <summary>Enables the SQL store kit's metrics by subscribing to the <see cref="SqlDiagnostics.SourceName" /> meter.</summary>
    /// <param name="builder">The <see cref="MeterProviderBuilder" /> being configured.</param>
    /// <returns>The same <paramref name="builder" /> for chaining.</returns>
    public static MeterProviderBuilder AddSqlInstrumentation(this MeterProviderBuilder builder)
    {
        Argument.IsNotNull(builder);

        return builder.AddMeter(SqlDiagnostics.SourceName);
    }
}
