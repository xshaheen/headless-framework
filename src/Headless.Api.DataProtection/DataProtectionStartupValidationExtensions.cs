// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Headless.Api;

/// <summary>Startup-validation extensions for <see cref="IDataProtectionBuilder"/>.</summary>
[PublicAPI]
public static class DataProtectionStartupValidationExtensions
{
    /// <summary>
    /// Registers an opt-in startup gate that validates the data-protection key ring at host startup, converting lazy
    /// first-write/rotation failures (missing container, bad credentials, missing write permission, misconfigured
    /// manager) into deploy-time failures.
    /// </summary>
    /// <param name="builder">The <see cref="IDataProtectionBuilder"/> to configure.</param>
    /// <param name="configure">
    /// Optional configuration for <see cref="DataProtectionStartupValidationOptions"/>; by default a validation
    /// failure throws (<see cref="StartupValidationMode.Throw"/>) and fails host startup, and the write probe
    /// (<see cref="DataProtectionStartupValidationOptions.ProbeWritePath"/>) is enabled.
    /// </param>
    /// <returns>The <paramref name="builder"/> so that additional calls can be chained.</returns>
    /// <remarks>
    /// <para>
    /// Key writes are lazy — the first write happens on first boot and again at the ~90-day key rotation — so a
    /// misconfigured container or manager can stay hidden for months post-deploy. At startup this probe behaves per
    /// <see cref="KeyManagementOptions.AutoGenerateKeys"/>:
    /// </para>
    /// <para>
    /// <b><c>AutoGenerateKeys == true</c> (default):</b> protects and unprotects a small payload through the real
    /// provider. On a fresh deployment this generates a key and drives the full persistence path (container ensure +
    /// blob upload), so any container/permission problem surfaces at boot.
    /// <b><c>AutoGenerateKeys == false</c> (designated-key-writer topologies):</b> performs a read-only probe via
    /// <c>IKeyManager.GetAllKeys()</c> — the repository read path is exercised but no key is ever generated. A
    /// reachable-but-empty key ring fails validation: the node would have no usable key for its first protected
    /// operation.
    /// </para>
    /// <para>
    /// In BOTH modes (unless <see cref="DataProtectionStartupValidationOptions.ProbeWritePath"/> is disabled), write
    /// access is verified with a real write: a reserved sentinel blob is uploaded to and deleted from the
    /// <c>DataProtection</c> container through the same ensure + retry pipeline the key writes use. Without it, a
    /// valid existing key means the round-trip performs no write and lost write permission stays hidden until
    /// rotation day.
    /// </para>
    /// <para>
    /// The service is an <c>IHostedLifecycleService</c> whose probe runs in <c>StartingAsync</c> — BEFORE any
    /// registered <see cref="IHostedService.StartAsync"/> — so a misconfigured key store gates the host before other
    /// services begin work. Registration is idempotent: calling this method twice registers a single hosted service;
    /// a second call's <paramref name="configure"/> still applies on top of the first.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IDataProtectionBuilder ValidateKeyRingAtStartup(
        this IDataProtectionBuilder builder,
        Action<DataProtectionStartupValidationOptions>? configure = null
    )
    {
        Argument.IsNotNull(builder);

        if (configure is not null)
        {
            builder.Services.Configure(configure);
        }

        // TryAddEnumerable keys on (service, implementation) so repeated calls never register the probe twice.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, DataProtectionStartupValidationService>()
        );

        return builder;
    }
}
