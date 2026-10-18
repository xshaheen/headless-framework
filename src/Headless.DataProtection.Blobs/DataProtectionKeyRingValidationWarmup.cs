// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Headless.DataProtection;

/// <summary>
/// Opt-in warm-up that forces the data-protection key ring through its persistence path once the host has started,
/// converting lazy first-write/rotation failures (missing container, bad credentials, misconfigured manager) into
/// deploy-time failures. Registered by <c>ValidateKeyRing</c>.
/// </summary>
/// <remarks>
/// <para>
/// Key writes are lazy: with blob-backed persistence the first write happens on first boot and again at the ~90-day
/// key rotation, so a misconfiguration can stay hidden for months post-deploy. When
/// <see cref="KeyManagementOptions.AutoGenerateKeys"/> is <see langword="true"/>, the validation protects and
/// unprotects a payload — on a fresh deployment this generates a key and drives <c>StoreElement</c> (container ensure +
/// upload) for real. When it is <see langword="false"/> (designated-key-writer topologies), it reads the key ring via
/// <see cref="IKeyManager.GetAllKeys"/> and never forces key generation; a reachable-but-empty key ring is a failure,
/// because the node's first protected operation would fail.
/// </para>
/// <para>
/// Unless <see cref="DataProtectionKeyRingValidationOptions.ProbeWritePath"/> is disabled, both modes additionally
/// verify write access with a real write: a reserved sentinel blob is uploaded to and deleted from the
/// <c>DataProtection</c> container through the same ensure + retry pipeline the key writes use. This is what
/// catches lost write permission when a valid key already exists (the round-trip performs no write then), and the
/// only write-path guarantee on read-only (<c>AutoGenerateKeys == false</c>) nodes.
/// </para>
/// <para>
/// The key ring is read one blob at a time through the synchronous <c>IXmlRepository</c> contract, so the validation
/// costs one listing plus one read per key, then the sentinel write. Running it as a warm-up keeps that cost off the
/// host start. Every step is safe to repeat: the round-trip reuses the key it created, and the sentinel is overwritten
/// and deleted.
/// </para>
/// </remarks>
internal sealed class DataProtectionKeyRingValidationWarmup(
    IServiceProvider serviceProvider,
    IOptions<DataProtectionKeyRingValidationOptions> validationOptions
) : IWarmup
{
    /// <summary>The protector purpose the round-trip probe uses.</summary>
    internal const string ValidationPurpose = "Headless.DataProtection.KeyRingValidation";

    /// <summary>The warm-up name the <c>warmup</c> readiness check and the logs report.</summary>
    internal const string WarmupName = "DataProtectionKeyRing";

    private readonly DataProtectionKeyRingValidationOptions _options = validationOptions.Value;

    public string Name => WarmupName;

    public bool GatesReadiness => _options.Mode is KeyRingValidationMode.GateReadiness;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Validation failed and <see cref="DataProtectionKeyRingValidationOptions.Mode"/> is
    /// <see cref="KeyRingValidationMode.GateReadiness"/>; for backend failures the original exception is preserved as
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

        var keyRingIsEmpty = false;

        try
        {
            var keyManagementOptions = serviceProvider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

            if (keyManagementOptions.AutoGenerateKeys)
            {
                // A protect/unprotect round-trip drives the key ring through the REAL persistence path: on a fresh
                // deployment no key exists yet, so this triggers key creation → StoreElement → container ensure →
                // upload, surfacing container/permission problems at deploy time instead of at the first lazy key
                // write.
                _RunProtectRoundTrip();
                logger.LogKeyRingValidationRoundTripSucceeded(BlobStorageDataProtectionXmlRepository.ContainerName);
            }
            else
            {
                // Designated-key-writer topologies: this node must never generate keys, so only exercise the
                // repository read path. Zero keys is a failure, not a pass: the backend answered, but the node has
                // no usable key ring and its first protected operation would fail.
                var keyCount = serviceProvider.GetRequiredService<IKeyManager>().GetAllKeys().Count;

                if (keyCount == 0)
                {
                    keyRingIsEmpty = true;
                }
                else
                {
                    logger.LogKeyRingValidationReadProbeSucceeded(
                        keyCount,
                        BlobStorageDataProtectionXmlRepository.ContainerName
                    );
                }
            }

            // The write probe runs in BOTH AutoGenerateKeys modes: with a valid key already present the round-trip
            // performs no write, and on read-only nodes nothing else touches the write path — this is the only
            // rotation-day write guarantee. Skipped when the key ring is already known-bad (empty).
            if (!keyRingIsEmpty && _options.ProbeWritePath)
            {
                await _RunWriteProbeAsync(logger, keyManagementOptions, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Any failure while the host is still running is a validation failure, including a backend timeout that
            // surfaces as an OperationCanceledException; only a stop request propagates as cancellation.
            if (_options.Mode is KeyRingValidationMode.LogOnly)
            {
                logger.LogKeyRingValidationFailed(exception, BlobStorageDataProtectionXmlRepository.ContainerName);

                return;
            }

            throw new InvalidOperationException(
                "Data protection key-ring validation failed: the key ring could not be exercised against the "
                    + $"'{BlobStorageDataProtectionXmlRepository.ContainerName}' blob container. On a fresh "
                    + "deployment this usually means the container does not exist or the credentials cannot access "
                    + "it. Wire an IBlobContainerManager so PersistKeysToBlobStorage ensures the container before "
                    + "writes, or provision the container out-of-band and acknowledge it with "
                    + "BlobContainerProvisioning.PreProvisioned. The host stays unready and the validation is retried; "
                    + "to log instead of gating readiness, set DataProtectionKeyRingValidationOptions.Mode = "
                    + "KeyRingValidationMode.LogOnly. Without this validation the same failure would surface at the "
                    + "first key write or the ~90-day key rotation. Cause: "
                    // The readiness check shows only this message, and the data-protection stack wraps the backend
                    // error in generic CryptographicException layers, so name the innermost cause.
                    + exception.GetBaseException().Message,
                exception
            );
        }

        if (!keyRingIsEmpty)
        {
            return;
        }

        // The empty-key-ring failure is handled OUTSIDE the catch so its actionable message surfaces directly
        // instead of being buried as an inner exception under the generic backend-failure wrap.
        if (_options.Mode is KeyRingValidationMode.LogOnly)
        {
            logger.LogKeyRingValidationEmptyKeyRing(BlobStorageDataProtectionXmlRepository.ContainerName);

            return;
        }

        throw new InvalidOperationException(
            "Data protection key-ring validation failed: the key-ring read probe reached the "
                + $"'{BlobStorageDataProtectionXmlRepository.ContainerName}' blob container but found no keys. "
                + "AutoGenerateKeys is false on this node (designated-key-writer topology), so it cannot create one "
                + "and its first protected operation would fail. This is not a backend error — the container is "
                + "reachable but empty: has the designated key writer run yet? Is this the right container/storage "
                + "for this environment? The host stays unready and the validation is retried."
        );
    }

    private void _RunProtectRoundTrip()
    {
        const string payload = "headless-dataprotection-key-ring-validation";

        var protector = serviceProvider
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(ValidationPurpose);

        var roundTripped = protector.Unprotect(protector.Protect(payload));

        if (!string.Equals(roundTripped, payload, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The data-protection round-trip returned a different payload than was protected."
            );
        }
    }

    private static async Task _RunWriteProbeAsync(
        ILogger logger,
        KeyManagementOptions keyManagementOptions,
        CancellationToken cancellationToken
    )
    {
        // The write probe is blob-repository-specific (sentinel upload + delete through the shared pipeline); with
        // any other IXmlRepository there is no equivalent generic write we could safely perform, so skip loudly
        // enough to be diagnosable.
        if (keyManagementOptions.XmlRepository is not BlobStorageDataProtectionXmlRepository repository)
        {
            logger.LogKeyRingValidationWriteProbeSkipped(
                keyManagementOptions.XmlRepository?.GetType().Name ?? "<none>"
            );

            return;
        }

        await repository.ProbeWriteAccessAsync(cancellationToken).ConfigureAwait(false);
        logger.LogKeyRingValidationWriteProbeSucceeded(BlobStorageDataProtectionXmlRepository.ContainerName);
    }
}
