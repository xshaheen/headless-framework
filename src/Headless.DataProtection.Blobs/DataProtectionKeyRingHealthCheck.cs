// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.Checks;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Headless.DataProtection;

/// <summary>
/// Readiness health check for the blob-backed data-protection key ring — the continuous complement to the
/// <c>ValidateKeyRing</c> warm-up: the warm-up validates once after the host starts, this check keeps validating on
/// every probe, catching a container deleted later (which the one-shot warm-up cannot see). Registered by
/// <see cref="DataProtectionHealthChecksExtensions.AddDataProtectionKeyRing"/>.
/// </summary>
/// <remarks>
/// <para>
/// The probe is selected by <see cref="KeyRingProbeStyle"/>, and each probe reports a distinct description so
/// operators can tell which ran:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="KeyRingProbeStyle.ContainerExistence"/> (default) — read-only. With a manager wired,
/// <see cref="IBlobContainerManager.ContainerExistsAsync"/> on the key container; without one, a listing that
/// requires at least one key. Does NOT verify write access.</description></item>
/// <item><description><see cref="KeyRingProbeStyle.WriteProbe"/> (explicit opt-in) — the sentinel write probe
/// (<see cref="BlobStorageDataProtectionXmlRepository.ProbeWriteAccessAsync"/>): a reserved sentinel blob is uploaded
/// and deleted through the retry pipeline the key writes use. Crash-safe — the sentinel is always excluded from
/// key-ring loading. <c>Healthy</c> means the key ring can actually be persisted (what rotation needs).</description></item>
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
    internal static string ExistenceProbeHealthyDescription(string containerName) =>
        $"The '{containerName}' blob container exists "
        + "(container-manager existence probe; write access not verified).";

    /// <summary>Description reported when the container-manager existence probe finds no container.</summary>
    internal static string ContainerMissingDescription(string containerName) =>
        $"The '{containerName}' blob container is missing; run 'resources provision -k "
        + $"{BlobStorageDataProtectionOptions.KeysResourceKind}' (container-manager existence probe).";

    /// <summary>Description reported when the container-manager existence probe itself throws.</summary>
    internal static string ExistenceProbeFailedDescription(string containerName) =>
        $"The '{containerName}' blob container existence check failed " + "(container-manager existence probe).";

    /// <summary>Description reported when the sentinel write probe succeeds.</summary>
    internal static string WriteProbeHealthyDescription(string containerName) =>
        $"Write access to the '{containerName}' blob container verified " + "(sentinel write probe: upload + delete).";

    /// <summary>Description reported when the sentinel write probe fails.</summary>
    internal static string WriteProbeFailedDescription(string containerName) =>
        $"Write access to the '{containerName}' blob container could not "
        + "be verified (sentinel write probe: upload + delete).";

    /// <summary>Description reported when the key listing (no manager wired) finds at least one key.</summary>
    internal static string KeyListingHealthyDescription(string containerName) =>
        $"The '{containerName}' blob container holds a data-protection key "
        + "(key listing; no IBlobContainerManager is wired; write access not verified).";

    /// <summary>Description reported when the key listing (no manager wired) finds no key.</summary>
    internal static string KeyListingEmptyDescription(string containerName) =>
        $"The '{containerName}' blob container holds no data-protection key; run 'resources provision -k "
        + $"{BlobStorageDataProtectionOptions.KeysResourceKind}' (key listing).";

    /// <summary>Description reported when the key listing (no manager wired) itself throws.</summary>
    internal static string KeyListingFailedDescription(string containerName) =>
        $"The '{containerName}' blob container could not be listed (key listing).";

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
        catch (Exception exception) when (exception is not OperationCanceledException)
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

        if (probeStyle is KeyRingProbeStyle.WriteProbe)
        {
            return await _RunWriteProbeAsync(
                    repository,
                    context,
                    WriteProbeHealthyDescription(repository.ContainerName),
                    WriteProbeFailedDescription(repository.ContainerName),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        if (repository.ContainerManager is { } containerManager)
        {
            return await _RunExistenceProbeAsync(containerManager, repository.ContainerName, context, cancellationToken)
                .ConfigureAwait(false);
        }

        // Pre-provisioned mode has no manager to ask, so a one-page key listing is the read that proves the container
        // is reachable and provisioned.
        return await _RunKeyListingProbeAsync(repository, context, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HealthCheckResult> _RunKeyListingProbeAsync(
        BlobStorageDataProtectionXmlRepository repository,
        HealthCheckContext context,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var keys = await repository.CountKeyBlobsAsync(maxCount: 1, cancellationToken).ConfigureAwait(false);

            return keys > 0
                ? HealthCheckResult.Healthy(KeyListingHealthyDescription(repository.ContainerName))
                : new HealthCheckResult(
                    context.Registration.FailureStatus,
                    KeyListingEmptyDescription(repository.ContainerName)
                );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                KeyListingFailedDescription(repository.ContainerName),
                exception
            );
        }
    }

    private static async Task<HealthCheckResult> _RunExistenceProbeAsync(
        IBlobContainerManager containerManager,
        string containerName,
        HealthCheckContext context,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var exists = await containerManager
                .ContainerExistsAsync(containerName, cancellationToken)
                .ConfigureAwait(false);

            return exists
                ? HealthCheckResult.Healthy(ExistenceProbeHealthyDescription(containerName))
                : new HealthCheckResult(context.Registration.FailureStatus, ContainerMissingDescription(containerName));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                ExistenceProbeFailedDescription(containerName),
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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, failedDescription, exception);
        }
    }
}
