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
    /// Registers an opt-in warm-up that reads the data-protection key ring once the host has started, so a missing
    /// container or key, bad credentials, or a misconfigured manager keeps the host unready at deploy time without
    /// delaying the start.
    /// </summary>
    /// <param name="builder">The <see cref="IDataProtectionBuilder"/> to configure.</param>
    /// <param name="configure">
    /// Optional configuration for <see cref="DataProtectionKeyRingValidationOptions"/>; by default a failure keeps the
    /// host unready (<see cref="KeyRingCheck.GateReadiness"/>).
    /// </param>
    /// <returns>The <paramref name="builder"/> so that additional calls can be chained.</returns>
    /// <remarks>
    /// <para>
    /// The validation writes nothing. With a container manager wired it checks that the key container exists, then it
    /// loads the key ring through <see cref="IKeyManager.GetAllKeys"/> and fails when the ring holds no key, whatever
    /// <see cref="KeyManagementOptions.AutoGenerateKeys"/> says: <c>resources provision</c> creates the container and
    /// the first key, never the host.
    /// </para>
    /// <para>
    /// The validation is an <c>IWarmup</c>: it runs in the background after the host has started, so reading the key
    /// blobs never delays the start. With <see cref="KeyRingCheck.GateReadiness"/> the <c>warmup</c> readiness check
    /// stays <c>Unhealthy</c>, reporting the validation error, and the validation is retried until it succeeds. With
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
