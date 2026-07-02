// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Headless.Api;

/// <summary>Health-check registration extensions for the blob-backed data-protection key ring.</summary>
[PublicAPI]
public static class DataProtectionHealthChecksExtensions
{
    /// <summary>The default registration name of the key-ring health check.</summary>
    public const string DefaultName = "dataprotection-keyring";

    /// <summary>
    /// Registers a readiness health check for the blob-backed data-protection key ring — the continuous
    /// complement to <c>ValidateKeyRingAtStartup</c>: the boot gate validates once before the host starts, this
    /// check keeps validating on every probe, catching a container deleted or write permission revoked AFTER boot.
    /// </summary>
    /// <param name="builder">The <see cref="IHealthChecksBuilder"/> to add the check to.</param>
    /// <param name="name">The health check name; defaults to <see cref="DefaultName"/>.</param>
    /// <param name="failureStatus">
    /// The <see cref="HealthStatus"/> reported when a probe fails; <see langword="null"/> means
    /// <see cref="HealthStatus.Unhealthy"/>. A non-blob <c>XmlRepository</c> always reports
    /// <see cref="HealthStatus.Degraded"/> (registration misuse, not an outage).
    /// </param>
    /// <param name="tags">Optional tags to filter the check by (e.g. a readiness tag).</param>
    /// <param name="probeStyle">
    /// Which probe runs on every health ping. The default, <see cref="KeyRingProbeStyle.WriteProbe"/>, verifies
    /// the full persistence path with a sentinel write + delete, manager or not — <c>Healthy</c> uniformly means
    /// "the key ring can be persisted". <see cref="KeyRingProbeStyle.ContainerExistence"/> is the explicit
    /// opt-down: a cheap container-existence check via the wired <see cref="Headless.Blobs.IBlobContainerManager"/>
    /// that does NOT verify write access (with no manager wired it falls back to the write probe and says so in
    /// its description).
    /// </param>
    /// <returns>The <paramref name="builder"/> so that additional calls can be chained.</returns>
    /// <remarks>
    /// With the default <see cref="KeyRingProbeStyle.WriteProbe"/> style, each probe performs a real sentinel
    /// write (upload + delete through the same ensure + retry pipeline the key writes use) — pair that with a
    /// probe interval you are comfortable with, or opt down to
    /// <see cref="KeyRingProbeStyle.ContainerExistence"/> and accept its weaker guarantee. The check deliberately
    /// does no caching or throttling of its own.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
    public static IHealthChecksBuilder AddDataProtectionKeyRing(
        this IHealthChecksBuilder builder,
        string name = DefaultName,
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null,
        KeyRingProbeStyle probeStyle = KeyRingProbeStyle.WriteProbe
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNullOrWhiteSpace(name);

        // A factory registration (instead of AddCheck<T>) carries the per-registration probe style while still
        // resolving IOptions<KeyManagementOptions> from DI per probe execution — no capture of a possibly-stale
        // repository at registration time.
        return builder.Add(
            new HealthCheckRegistration(
                name,
                provider => new DataProtectionKeyRingHealthCheck(
                    provider.GetRequiredService<IOptions<KeyManagementOptions>>(),
                    probeStyle
                ),
                failureStatus,
                tags
            )
        );
    }
}
