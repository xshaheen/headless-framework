// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Headless.DataProtection;

/// <summary>
/// Opt-in warm-up that reads the data-protection key ring once the host has started, so a missing container, missing
/// key, bad credentials, or misconfigured wiring keeps the host unready at deploy time instead of failing the first
/// protected operation. Registered by <c>ValidateKeyRing</c>.
/// </summary>
/// <remarks>
/// <para>
/// It only reads: when a container manager is wired, one container-existence check, then the key ring through
/// <see cref="IKeyManager.GetAllKeys"/>. It never protects a payload, because on an empty ring that generates and
/// writes a key, and it never writes a probe blob. A ring with no key fails: <c>resources provision</c> creates the
/// first one.
/// </para>
/// <para>
/// The key ring is read one blob at a time through the synchronous <c>IXmlRepository</c> contract, so the validation
/// costs one listing plus one read per key. Running it as a warm-up keeps that cost off the host start.
/// </para>
/// </remarks>
internal sealed class DataProtectionKeyRingValidationWarmup(
    IServiceProvider serviceProvider,
    IOptions<DataProtectionKeyRingValidationOptions> validationOptions
) : IWarmup
{
    /// <summary>The warm-up name the <c>warmup</c> readiness check and the logs report.</summary>
    internal const string WarmupName = "DataProtectionKeyRing";

    private readonly DataProtectionKeyRingValidationOptions _options = validationOptions.Value;

    public string Name => WarmupName;

    public bool GatesReadiness => _options.Check is KeyRingCheck.GateReadiness;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Validation failed and <see cref="DataProtectionKeyRingValidationOptions.Check"/> is
    /// <see cref="KeyRingCheck.GateReadiness"/>; for backend failures the original exception is preserved as
    /// the inner exception. The runner reports the message on the readiness check and retries.
    /// </exception>
    public async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        // The logger and every key-ring dependency are resolved inside the guarded block (not constructor injected):
        // materializing KeyManagementOptions / IKeyManager can itself throw the misconfiguration this validation
        // exists to surface (e.g. the PersistKeysToBlobStorage provisioning guardrail throws at first options
        // resolution), and that failure must be reported like any other instead of failing the warm-up's construction.
        var logger =
            serviceProvider.GetService<ILoggerFactory>()?.CreateLogger(typeof(DataProtectionKeyRingValidationWarmup))
            ?? NullLogger.Instance;

        var containerName = _ConfiguredContainerName();
        string? missingResource;
        var keyCount = 0;

        try
        {
            var keyManagementOptions = serviceProvider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
            var blobRepository = keyManagementOptions.XmlRepository as BlobStorageDataProtectionXmlRepository;

            if (blobRepository is not null)
            {
                containerName = blobRepository.ContainerName;
            }

            if (
                blobRepository?.ContainerManager is { } containerManager
                && !await containerManager.ContainerExistsAsync(containerName, cancellationToken).ConfigureAwait(false)
            )
            {
                missingResource = $"the '{containerName}' blob container does not exist";
            }
            else
            {
                keyCount = serviceProvider.GetRequiredService<IKeyManager>().GetAllKeys().Count;
                missingResource = keyCount == 0 ? $"the '{containerName}' blob container holds no key" : null;
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Any failure while the host is still running is a validation failure, including a backend timeout that
            // surfaces as an OperationCanceledException; only a stop request propagates as cancellation.
            if (_options.Check is KeyRingCheck.LogOnly)
            {
                logger.LogKeyRingValidationFailed(exception, containerName);

                return;
            }

            throw new InvalidOperationException(
                "Data protection key-ring validation failed: the key ring could not be read from the "
                    + $"'{containerName}' blob container. Verify the credentials can read it, and that "
                    + $"'resources provision -k {BlobStorageDataProtectionOptions.KeysResourceKind}' ran for this "
                    + "environment. The host stays unready and the validation is retried; to log instead of gating "
                    + "readiness, set DataProtectionKeyRingValidationOptions.Check = KeyRingCheck.LogOnly. Cause: "
                    // The readiness check shows only this message, and the data-protection stack wraps the backend
                    // error in generic CryptographicException layers, so name the innermost cause.
                    + exception.GetBaseException().Message,
                exception
            );
        }

        if (missingResource is null)
        {
            logger.LogKeyRingValidationSucceeded(keyCount, containerName);

            return;
        }

        // Handled OUTSIDE the catch so the actionable message surfaces directly instead of being buried as an inner
        // exception under the generic backend-failure wrap.
        if (_options.Check is KeyRingCheck.LogOnly)
        {
            logger.LogKeyRingValidationNotProvisioned(containerName, missingResource);

            return;
        }

        throw new InvalidOperationException(
            $"Data protection key-ring validation failed: {missingResource}. The host never creates the container or "
                + $"the first key: run 'resources provision -k {BlobStorageDataProtectionOptions.KeysResourceKind}' "
                + "for this environment. The host stays unready and the validation is retried."
        );
    }

    // The name a failure message reports before the repository itself is reachable. Reading the options can throw
    // the same misconfiguration this validation reports, so that falls back to the default name.
    private string _ConfiguredContainerName()
    {
        try
        {
            return serviceProvider.GetService<IOptions<BlobStorageDataProtectionOptions>>()?.Value.ContainerName
                ?? BlobStorageDataProtectionOptions.DefaultContainerName;
        }
        catch (OptionsValidationException)
        {
            return BlobStorageDataProtectionOptions.DefaultContainerName;
        }
    }
}
