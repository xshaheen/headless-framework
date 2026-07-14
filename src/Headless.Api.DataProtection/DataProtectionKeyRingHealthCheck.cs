// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.Checks;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Headless.Api.DataProtection;

/// <summary>
/// Readiness health check for the blob-backed data-protection key ring — the continuous complement to the
/// <c>ValidateKeyRingAtStartup</c> boot gate: the gate validates once before the host starts, this check keeps
/// validating on every probe, catching a container deleted or write permission revoked AFTER boot (which the
/// one-shot gate cannot see). Registered by
/// <see cref="DataProtectionHealthChecksExtensions.AddDataProtectionKeyRing"/>.
/// </summary>
/// <remarks>
/// <para>
/// The probe is selected by <see cref="KeyRingProbeStyle"/>, not by the wiring, so <c>Healthy</c> has ONE meaning
/// per registration (each probe reports a distinct description so operators can tell which ran):
/// </para>
/// <list type="bullet">
/// <item><description><see cref="KeyRingProbeStyle.WriteProbe"/> (default) — the definitive sentinel write probe
/// (<see cref="BlobStorageDataProtectionXmlRepository.ProbeWriteAccessAsync"/>): a reserved sentinel blob is
/// uploaded and deleted through the same ensure + retry pipeline the key writes use, manager or not. Crash-safe —
/// the sentinel is always excluded from key-ring loading. <c>Healthy</c> means the key ring can actually be
/// persisted (what rotation needs).</description></item>
/// <item><description><see cref="KeyRingProbeStyle.ContainerExistence"/> (explicit opt-down) — a cheap
/// <see cref="IBlobContainerManager.ContainerExistsAsync"/> existence check on the <c>DataProtection</c>
/// container. Does NOT verify write access; a missing container means key rotation will fail. When no manager is
/// wired, the check falls back to the write probe (the only probe possible) with a description noting the
/// fallback.</description></item>
/// </list>
/// <para>
/// When the configured <see cref="KeyManagementOptions.XmlRepository"/> is not the blob-backed repository, the
/// check reports <see cref="HealthStatus.Degraded"/>: that is registration misuse (nothing to check), not an
/// outage. Probe failures report the registration's failure status (<see cref="HealthStatus.Unhealthy"/> by
/// default) with the probe exception attached.
/// </para>
/// </remarks>
internal sealed class DataProtectionKeyRingHealthCheck(
    IOptions<KeyManagementOptions> keyManagementOptions,
    KeyRingProbeStyle probeStyle
) : IHealthCheck
{
    /// <summary>Description reported when the container-manager existence probe finds the container.</summary>
    internal const string ExistenceProbeHealthyDescription =
        $"The '{BlobStorageDataProtectionXmlRepository.ContainerName}' blob container exists "
        + "(container-manager existence probe; write access not verified).";

    /// <summary>Description reported when the container-manager existence probe finds no container.</summary>
    internal const string ContainerMissingDescription =
        $"The '{BlobStorageDataProtectionXmlRepository.ContainerName}' blob container is missing — key rotation "
        + "will fail (container-manager existence probe).";

    /// <summary>Description reported when the container-manager existence probe itself throws.</summary>
    internal const string ExistenceProbeFailedDescription =
        $"The '{BlobStorageDataProtectionXmlRepository.ContainerName}' blob container existence check failed "
        + "(container-manager existence probe).";

    /// <summary>Description reported when the sentinel write probe succeeds.</summary>
    internal const string WriteProbeHealthyDescription =
        $"Write access to the '{BlobStorageDataProtectionXmlRepository.ContainerName}' blob container verified "
        + "(sentinel write probe: upload + delete).";

    /// <summary>Description reported when the sentinel write probe fails.</summary>
    internal const string WriteProbeFailedDescription =
        $"Write access to the '{BlobStorageDataProtectionXmlRepository.ContainerName}' blob container could not "
        + "be verified (sentinel write probe: upload + delete).";

    /// <summary>
    /// Description reported when a <see cref="KeyRingProbeStyle.ContainerExistence"/> registration succeeded via
    /// the write-probe fallback because no container manager is wired.
    /// </summary>
    internal const string ExistenceFallbackHealthyDescription =
        $"Write access to the '{BlobStorageDataProtectionXmlRepository.ContainerName}' blob container verified "
        + "(sentinel write probe — the requested container-existence probe is unavailable because no "
        + "IBlobContainerManager is wired).";

    /// <summary>
    /// Description reported when a <see cref="KeyRingProbeStyle.ContainerExistence"/> registration failed via the
    /// write-probe fallback because no container manager is wired.
    /// </summary>
    internal const string ExistenceFallbackFailedDescription =
        $"Write access to the '{BlobStorageDataProtectionXmlRepository.ContainerName}' blob container could not "
        + "be verified (sentinel write probe — the requested container-existence probe is unavailable because no "
        + "IBlobContainerManager is wired).";

    /// <summary>Description reported when <see cref="KeyManagementOptions"/> cannot be materialized.</summary>
    internal const string OptionsResolutionFailedDescription =
        "KeyManagementOptions could not be materialized — the data-protection persistence configuration itself "
        + "is invalid (e.g. the PersistKeysToBlobStorage provisioning guardrail rejected the wiring).";

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(context);

        IXmlRepository? xmlRepository;

        try
        {
            // Materializing KeyManagementOptions can itself throw the misconfiguration this check exists to
            // surface (the PersistKeysToBlobStorage provisioning guardrail throws at first options resolution
            // for the DI/factory/keyed overloads), so it is resolved inside the guarded region instead of the
            // constructor — a broken wiring must report Unhealthy, not crash the health-check pipeline's DI
            // activation.
            xmlRepository = keyManagementOptions.Value.XmlRepository;
        }
#pragma warning disable CA1031 // Health-check boundary: any resolution failure becomes an Unhealthy result carrying the exception; nothing is swallowed. Cancellation is excluded by the filter so it propagates untouched.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                OptionsResolutionFailedDescription,
                exception
            );
        }

        if (xmlRepository is not BlobStorageDataProtectionXmlRepository repository)
        {
            // Registration misuse, not an outage: the key ring is simply not blob-backed on this host, so there
            // is nothing this check can probe — Degraded keeps it visible without paging anyone for a non-failure.
            return HealthCheckResult.Degraded(
                "The data-protection key ring is not persisting to blob storage (XmlRepository is "
                    + $"'{xmlRepository?.GetType().Name ?? "<none>"}'), so there is nothing to check. Register "
                    + "PersistKeysToBlobStorage or remove this health check."
            );
        }

        if (probeStyle is KeyRingProbeStyle.ContainerExistence)
        {
            if (repository.ContainerManager is { } containerManager)
            {
                return await _RunExistenceProbeAsync(containerManager, context, cancellationToken)
                    .ConfigureAwait(false);
            }

            // The consumer opted down to the existence probe but no manager is wired (pre-provisioned mode), so
            // the stronger write probe is the only probe possible — fall back to it (with a description noting
            // the fallback) rather than degrading a legitimate wiring.
            return await _RunWriteProbeAsync(
                    repository,
                    context,
                    ExistenceFallbackHealthyDescription,
                    ExistenceFallbackFailedDescription,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        // Default (WriteProbe): the definitive probe runs manager or not, so Healthy uniformly means "the key
        // ring can be persisted" — an existence-only manager answer must never mask revoked write access.
        return await _RunWriteProbeAsync(
                repository,
                context,
                WriteProbeHealthyDescription,
                WriteProbeFailedDescription,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static async Task<HealthCheckResult> _RunExistenceProbeAsync(
        IBlobContainerManager containerManager,
        HealthCheckContext context,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var exists = await containerManager
                .ContainerExistsAsync(BlobStorageDataProtectionXmlRepository.ContainerName, cancellationToken)
                .ConfigureAwait(false);

            return exists
                ? HealthCheckResult.Healthy(ExistenceProbeHealthyDescription)
                : new HealthCheckResult(context.Registration.FailureStatus, ContainerMissingDescription);
        }
#pragma warning disable CA1031 // Health-check boundary: any backend failure (provider exception types this package deliberately does not reference) becomes an Unhealthy result carrying the exception. Cancellation is excluded by the filter so it propagates untouched.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                ExistenceProbeFailedDescription,
                exception
            );
        }
    }

    private static async Task<HealthCheckResult> _RunWriteProbeAsync(
        BlobStorageDataProtectionXmlRepository repository,
        HealthCheckContext context,
        string healthyDescription,
        string failedDescription,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await repository.ProbeWriteAccessAsync(cancellationToken).ConfigureAwait(false);

            return HealthCheckResult.Healthy(healthyDescription);
        }
#pragma warning disable CA1031 // Health-check boundary: the probe wraps terminal backend failures in InvalidOperationException, but this catch stays broad for symmetry with the existence path — any failure becomes an Unhealthy result carrying the exception. Cancellation is excluded by the filter so it propagates untouched.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return new HealthCheckResult(context.Registration.FailureStatus, failedDescription, exception);
        }
    }
}
