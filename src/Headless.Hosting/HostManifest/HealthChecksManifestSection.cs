// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Headless.Hosting;

/// <summary>
/// Writes the <c>healthChecks</c> section every host manifest carries: each health check the host registered, with
/// the tags that route it to an endpoint and the status it reports on failure, so an operator sees what readiness
/// probes before the host runs.
/// </summary>
/// <remarks>
/// The registrations are read from the configured <see cref="HealthCheckServiceOptions" />, after every Headless rule
/// applied, so a check turned off or folded into another is absent here exactly as it is from the health endpoints.
/// Reading them creates no check and runs no probe.
/// </remarks>
internal sealed class HealthChecksManifestSection(IServiceProvider services) : IHostManifestSection
{
    public string Name => "healthChecks";

    public void Write(Utf8JsonWriter writer)
    {
        var registrations = services.GetService<IOptions<HealthCheckServiceOptions>>()?.Value.Registrations ?? [];

        writer.WriteStartArray();

        foreach (var registration in registrations.OrderBy(static x => x.Name, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("name", registration.Name);
            writer.WriteStartArray("tags");

            foreach (var tag in registration.Tags.Order(StringComparer.Ordinal))
            {
                writer.WriteStringValue(tag);
            }

            writer.WriteEndArray();
            writer.WriteString("failureStatus", _Status(registration.FailureStatus));

            if (registration.Timeout != Timeout.InfiniteTimeSpan)
            {
                writer.WriteNumber("timeoutSeconds", registration.Timeout.TotalSeconds);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static string _Status(HealthStatus status) =>
        status switch
        {
            HealthStatus.Unhealthy => "unhealthy",
            HealthStatus.Degraded => "degraded",
            HealthStatus.Healthy => "healthy",
            _ => ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
}
