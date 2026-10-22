// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.DataProtection;

/// <summary>Key-ring validation extensions for <see cref="IDataProtectionBuilder"/>.</summary>
[PublicAPI]
public static class DataProtectionKeyRingValidationExtensions
{
    /// <summary>
    /// Registers an opt-in warm-up that validates the data-protection key ring once the host has started, converting
    /// lazy first-write/rotation failures (missing container, bad credentials, missing write permission, misconfigured
    /// manager) into deploy-time failures without delaying the start.
    /// </summary>
    /// <param name="builder">The <see cref="IDataProtectionBuilder"/> to configure.</param>
    /// <param name="configure">
    /// Optional configuration for <see cref="DataProtectionKeyRingValidationOptions"/>; by default a failure keeps the
    /// host unready (<see cref="KeyRingCheck.GateReadiness"/>) and the write probe
    /// (<see cref="DataProtectionKeyRingValidationOptions.ProbeWritePath"/>) is enabled.
    /// </param>
    /// <returns>The <paramref name="builder"/> so that additional calls can be chained.</returns>
    /// <remarks>
    /// <para>
    /// Key writes are lazy — the first write happens on first boot and again at the ~90-day key rotation — so a
    /// misconfigured container or manager can stay hidden for months post-deploy. The validation behaves per
    /// <see cref="KeyManagementOptions.AutoGenerateKeys"/>:
    /// </para>
    /// <para>
    /// <b><c>AutoGenerateKeys == true</c> (default):</b> protects and unprotects a small payload through the real
    /// provider. On a fresh deployment this generates a key and drives the full persistence path (container ensure +
    /// blob upload), so any container/permission problem surfaces at deploy time.
    /// <b><c>AutoGenerateKeys == false</c> (designated-key-writer topologies):</b> performs a read-only probe via
    /// <c>IKeyManager.GetAllKeys()</c> — the repository read path is exercised but no key is ever generated. A
    /// reachable-but-empty key ring fails validation: the node would have no usable key for its first protected
    /// operation.
    /// </para>
    /// <para>
    /// In BOTH modes (unless <see cref="DataProtectionKeyRingValidationOptions.ProbeWritePath"/> is disabled), write
    /// access is verified with a real write: a reserved sentinel blob is uploaded to and deleted from the
    /// <c>DataProtection</c> container through the same ensure + retry pipeline the key writes use. Without it, a
    /// valid existing key means the round-trip performs no write and lost write permission stays hidden until
    /// rotation day.
    /// </para>
    /// <para>
    /// The validation is an <c>IWarmup</c>: it runs in the background after the host has started, so reading the key
    /// blobs and writing the sentinel never delay the start. With
    /// <see cref="KeyRingCheck.GateReadiness"/> the <c>warmup</c> readiness check stays <c>Unhealthy</c>,
    /// reporting the validation error, and the validation is retried until it succeeds. With
    /// <see cref="KeyRingCheck.LogOnly"/> it runs once and never gates readiness. Registration is idempotent:
    /// calling this method twice registers a single warm-up; a second call's <paramref name="configure"/> still
    /// applies on top of the first.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IDataProtectionBuilder ValidateKeyRing(
        this IDataProtectionBuilder builder,
        Action<DataProtectionKeyRingValidationOptions>? configure = null
    )
    {
        Argument.IsNotNull(builder);

        if (configure is not null)
        {
            builder.Services.Configure(configure);
        }

        builder.Services.AddWarmup<DataProtectionKeyRingValidationWarmup>();

        return builder;
    }
}
