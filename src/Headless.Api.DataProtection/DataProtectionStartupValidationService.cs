// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Headless.Api;

/// <summary>
/// Opt-in startup probe that forces the data-protection key ring through its persistence path at host start,
/// converting lazy first-write/rotation failures (missing container, bad credentials, misconfigured manager) into
/// deploy-time failures. Registered by <c>ValidateKeyRingAtStartup</c>.
/// </summary>
/// <remarks>
/// <para>
/// Key writes are lazy: with blob-backed persistence the first write happens on first boot and again at the ~90-day
/// key rotation, so a misconfiguration can stay hidden for months post-deploy. When
/// <see cref="KeyManagementOptions.AutoGenerateKeys"/> is <see langword="true"/>, the probe protects and unprotects a
/// payload — on a fresh deployment this generates a key and drives <c>StoreElement</c> (container ensure + upload)
/// for real. When it is <see langword="false"/> (designated-key-writer topologies), the probe reads the key ring via
/// <see cref="IKeyManager.GetAllKeys"/> and never forces key generation; a reachable-but-empty key ring is a
/// validation failure, because the node's first protected operation would fail.
/// </para>
/// <para>
/// Unless <see cref="DataProtectionStartupValidationOptions.ProbeWritePath"/> is disabled, both modes additionally
/// verify write access with a real write: a reserved sentinel blob is uploaded to and deleted from the
/// <c>DataProtection</c> container through the same ensure + retry pipeline the key writes use. This is what
/// catches lost write permission when a valid key already exists (the round-trip performs no write then), and the
/// only write-path guarantee on read-only (<c>AutoGenerateKeys == false</c>) nodes.
/// </para>
/// <para>
/// Implemented as an <see cref="IHostedLifecycleService"/> whose work runs in <see cref="StartingAsync"/>, which
/// executes BEFORE any registered <see cref="IHostedService.StartAsync"/> (including the framework's own
/// data-protection hosted service and application services), so a misconfigured key store gates the host before
/// anything else starts. <see cref="StartAsync"/> and <see cref="StopAsync"/> are no-ops.
/// </para>
/// </remarks>
internal sealed class DataProtectionStartupValidationService(
    IServiceProvider serviceProvider,
    IOptions<DataProtectionStartupValidationOptions> validationOptions
) : IHostedLifecycleService
{
    /// <summary>The protector purpose the startup round-trip probe uses.</summary>
    internal const string ValidationPurpose = "Headless.Api.DataProtection.StartupValidation";

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Validation failed and <see cref="DataProtectionStartupValidationOptions.Mode"/> is
    /// <see cref="StartupValidationMode.Throw"/>; for backend failures the original exception is preserved as the
    /// inner exception.
    /// </exception>
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        // The logger and every key-ring dependency are resolved lazily inside the guarded block (not constructor
        // injected): materializing KeyManagementOptions / IKeyManager can itself throw the misconfiguration this
        // service exists to surface (e.g. the PersistKeysToBlobStorage provisioning guardrail throws at first
        // options resolution), and LogOnly mode must observe those failures instead of crashing the host while DI
        // constructs the hosted service.
        var logger =
            serviceProvider.GetService<ILoggerFactory>()?.CreateLogger(typeof(DataProtectionStartupValidationService))
            ?? NullLogger.Instance;

        var options = validationOptions.Value;
        var keyRingIsEmpty = false;

        try
        {
            var keyManagementOptions = serviceProvider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

            if (keyManagementOptions.AutoGenerateKeys)
            {
                // A protect/unprotect round-trip drives the key ring through the REAL persistence path: on a fresh
                // deployment no key exists yet, so this triggers key creation → StoreElement → container ensure →
                // upload, surfacing container/permission problems at boot instead of at the first lazy key write.
                _RunProtectRoundTrip();
                logger.LogStartupValidationRoundTripSucceeded(BlobStorageDataProtectionXmlRepository.ContainerName);
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
                    logger.LogStartupValidationReadProbeSucceeded(
                        keyCount,
                        BlobStorageDataProtectionXmlRepository.ContainerName
                    );
                }
            }

            // The write probe runs in BOTH AutoGenerateKeys modes: with a valid key already present the round-trip
            // performs no write, and on read-only nodes nothing else touches the write path — this is the only
            // rotation-day write guarantee. Skipped when the key ring is already known-bad (empty).
            if (!keyRingIsEmpty && options.ProbeWritePath)
            {
                await _RunWriteProbeAsync(logger, keyManagementOptions, cancellationToken).ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // Last-resort startup boundary: any failure kind (backend exception, provisioning-guardrail InvalidOperationException, crypto failure) must flow into the configured mode; the original exception is preserved as the inner exception (Throw) or logged in full (LogOnly). Host-shutdown cancellation is excluded by the filter so it propagates untouched.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            if (options.Mode is StartupValidationMode.LogOnly)
            {
                logger.LogStartupValidationFailed(exception, BlobStorageDataProtectionXmlRepository.ContainerName);

                return;
            }

            throw new InvalidOperationException(
                "Data protection startup validation failed: the key ring could not be exercised against the "
                    + $"'{BlobStorageDataProtectionXmlRepository.ContainerName}' blob container. On a fresh "
                    + "deployment this usually means the container does not exist or the credentials cannot access "
                    + "it. Wire an IBlobContainerManager so PersistKeysToBlobStorage ensures the container before "
                    + "writes, or provision the container out-of-band and acknowledge it with "
                    + "BlobContainerProvisioning.PreProvisioned. To log instead of failing startup, set "
                    + "DataProtectionStartupValidationOptions.Mode = StartupValidationMode.LogOnly. Without this "
                    + "validation the same failure would surface at the first key write or the ~90-day key rotation.",
                exception
            );
        }

        if (!keyRingIsEmpty)
        {
            return;
        }

        // The empty-key-ring failure is handled OUTSIDE the catch so its actionable message surfaces directly
        // instead of being buried as an inner exception under the generic backend-failure wrap.
        if (options.Mode is StartupValidationMode.LogOnly)
        {
            logger.LogStartupValidationEmptyKeyRing(BlobStorageDataProtectionXmlRepository.ContainerName);

            return;
        }

        throw new InvalidOperationException(
            "Data protection startup validation failed: the key-ring read probe reached the "
                + $"'{BlobStorageDataProtectionXmlRepository.ContainerName}' blob container but found no keys. "
                + "AutoGenerateKeys is false on this node (designated-key-writer topology), so it cannot create one "
                + "and its first protected operation would fail. This is not a backend error — the container is "
                + "reachable but empty: has the designated key writer run yet? Is this the right container/storage "
                + "for this environment?"
        );
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void _RunProtectRoundTrip()
    {
        const string payload = "headless-dataprotection-startup-validation";

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
            logger.LogStartupValidationWriteProbeSkipped(
                keyManagementOptions.XmlRepository?.GetType().Name ?? "<none>"
            );

            return;
        }

        await repository.ProbeWriteAccessAsync(cancellationToken).ConfigureAwait(false);
        logger.LogStartupValidationWriteProbeSucceeded(BlobStorageDataProtectionXmlRepository.ContainerName);
    }
}
