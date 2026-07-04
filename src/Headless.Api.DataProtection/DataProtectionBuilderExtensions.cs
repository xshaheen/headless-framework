// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.Checks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Api;

[PublicAPI]
public static class DataProtectionBuilderExtensions
{
    /// <summary>
    /// Configures the data protection system to persist XML key descriptors to an <see cref="IBlobStorage"/> backend.
    /// </summary>
    /// <param name="builder">The <see cref="IDataProtectionBuilder"/> to configure.</param>
    /// <param name="storage">The blob storage instance that will store the key XML files.</param>
    /// <param name="loggerFactory">
    /// Optional logger factory passed to the repository; when <see langword="null"/>, logging is suppressed.
    /// </param>
    /// <param name="provisioning">
    /// Pass <see cref="BlobContainerProvisioning.PreProvisioned"/> to acknowledge that the <c>DataProtection</c>
    /// container was already provisioned out-of-band (portal, CLI, IaC), suppressing the configuration-time guardrail
    /// for backends that require provisioning. Required for providers that deliberately ship no container manager,
    /// such as Cloudflare R2 (object-scoped tokens cannot create buckets). Ignored by backends whose
    /// <see cref="IBlobStorage.RequiresContainerProvisioning"/> is <see langword="false"/> (Redis).
    /// </param>
    /// <returns>The <paramref name="builder"/> so that additional calls can be chained.</returns>
    /// <remarks>
    /// <b>The <c>DataProtection</c> container is NOT ensured by this overload.</b> The blob data plane treats a
    /// missing container as an error rather than auto-creating it, so on a fresh Azure/S3/file-system deployment the
    /// first key write fails unless the container already exists. This is enforced at configuration time: when
    /// <paramref name="storage"/> reports <see cref="IBlobStorage.RequiresContainerProvisioning"/> as
    /// <see langword="true"/>, this overload throws unless <paramref name="provisioning"/> acknowledges out-of-band
    /// provisioning. Prefer
    /// <see cref="PersistKeysToBlobStorage(IDataProtectionBuilder, IBlobStorage, IBlobContainerManager, ILoggerFactory)"/>
    /// which ensures the container before writes. Lazily-materializing backends (Redis) report
    /// <see cref="IBlobStorage.RequiresContainerProvisioning"/> as <see langword="false"/> and configure without any
    /// acknowledgment.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="storage"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="storage"/> requires container provisioning and <paramref name="provisioning"/> is not
    /// <see cref="BlobContainerProvisioning.PreProvisioned"/> — the first key write would fail against the missing
    /// container.
    /// </exception>
    public static IDataProtectionBuilder PersistKeysToBlobStorage(
        this IDataProtectionBuilder builder,
        IBlobStorage storage,
        ILoggerFactory? loggerFactory = null,
        BlobContainerProvisioning provisioning = BlobContainerProvisioning.Managed
    )
    {
        Argument.IsNotNull(storage);
        _EnsureProvisioningIsCovered(storage, containerManager: null, provisioning);

        builder.Services.Configure<KeyManagementOptions>(options =>
            options.XmlRepository = new BlobStorageDataProtectionXmlRepository(storage, loggerFactory)
        );

        return builder;
    }

    /// <summary>
    /// Configures the data protection system to persist XML key descriptors to an <see cref="IBlobStorage"/> backend
    /// and ensure the key container through <paramref name="containerManager"/> before writes.
    /// </summary>
    /// <param name="builder">The <see cref="IDataProtectionBuilder"/> to configure.</param>
    /// <param name="storage">The blob storage instance that will store the key XML files.</param>
    /// <param name="containerManager">Container manager used to ensure the DataProtection container before writes.</param>
    /// <param name="loggerFactory">
    /// Optional logger factory passed to the repository; when <see langword="null"/>, logging is suppressed.
    /// </param>
    /// <returns>The <paramref name="builder"/> so that additional calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/>, <paramref name="storage"/>, or <paramref name="containerManager"/> is
    /// <see langword="null"/>.
    /// </exception>
    public static IDataProtectionBuilder PersistKeysToBlobStorage(
        this IDataProtectionBuilder builder,
        IBlobStorage storage,
        IBlobContainerManager containerManager,
        ILoggerFactory? loggerFactory = null
    )
    {
        builder.Services.Configure<KeyManagementOptions>(options =>
        {
            options.XmlRepository = new BlobStorageDataProtectionXmlRepository(
                storage,
                containerManager,
                loggerFactory
            );
        });

        return builder;
    }

    /// <summary>
    /// Configures the data protection system to persist XML key descriptors to an <see cref="IBlobStorage"/> backend
    /// resolved from the application's DI container at first use.
    /// </summary>
    /// <param name="builder">The <see cref="IDataProtectionBuilder"/> to configure.</param>
    /// <param name="storageFactory">
    /// A factory delegate that receives the application's <see cref="IServiceProvider"/> and returns the
    /// <see cref="IBlobStorage"/> instance to use. Invoked once when the <c>KeyManagementOptions</c> are first configured.
    /// </param>
    /// <param name="containerManagerFactory">
    /// Optional factory resolving the <see cref="IBlobContainerManager"/> that ensures the key container before writes.
    /// Supply this when the storage is a keyed/named registration so the matching keyed manager is used; when
    /// <see langword="null"/> the unkeyed <see cref="IBlobContainerManager"/> is resolved (or none, if unregistered).
    /// </param>
    /// <param name="provisioning">
    /// Pass <see cref="BlobContainerProvisioning.PreProvisioned"/> to acknowledge that the <c>DataProtection</c>
    /// container was already provisioned out-of-band (portal, CLI, IaC). Only consulted when the effective container
    /// manager resolves to <see langword="null"/> and the resolved storage requires provisioning; see the
    /// <see cref="InvalidOperationException"/> remarks.
    /// </param>
    /// <returns>The <paramref name="builder"/> so that additional calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="storageFactory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown at first resolution (when the factories run) if the effective container manager is
    /// <see langword="null"/>, the resolved storage requires container provisioning
    /// (<see cref="IBlobStorage.RequiresContainerProvisioning"/>), and <paramref name="provisioning"/> is not
    /// <see cref="BlobContainerProvisioning.PreProvisioned"/> — the first key write would fail against the missing
    /// container.
    /// </exception>
    public static IDataProtectionBuilder PersistKeysToBlobStorage(
        this IDataProtectionBuilder builder,
        Func<IServiceProvider, IBlobStorage> storageFactory,
        Func<IServiceProvider, IBlobContainerManager?>? containerManagerFactory = null,
        BlobContainerProvisioning provisioning = BlobContainerProvisioning.Managed
    )
    {
        builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(services =>
        {
            var storage = storageFactory.Invoke(services);
            // Only fall back to the unkeyed manager when NO factory was supplied. When a factory is supplied its result
            // is authoritative even if null (a keyed store whose manager resolves to null must not silently ensure the
            // unkeyed default store's container — that mutates the wrong backend and still fails the keyed write).
            var containerManager = containerManagerFactory is null
                ? services.GetService<IBlobContainerManager>()
                : containerManagerFactory.Invoke(services);
            var loggerFactory = services.GetService<ILoggerFactory>();

            _EnsureProvisioningIsCovered(storage, containerManager, provisioning);

            return new ConfigureOptions<KeyManagementOptions>(options =>
                options.XmlRepository = new BlobStorageDataProtectionXmlRepository(
                    storage,
                    containerManager,
                    loggerFactory
                )
            );
        });

        return builder;
    }

    /// <summary>
    /// Configures the data protection system to persist XML key descriptors to a <em>keyed</em>
    /// <see cref="IBlobStorage"/> backend registered under <paramref name="serviceKey"/>, ensuring its key container
    /// through the matching keyed <see cref="IBlobContainerManager"/> (when registered) before writes.
    /// </summary>
    /// <param name="builder">The <see cref="IDataProtectionBuilder"/> to configure.</param>
    /// <param name="serviceKey">The DI service key the blob storage (and its container manager) are registered under.</param>
    /// <param name="provisioning">
    /// Pass <see cref="BlobContainerProvisioning.PreProvisioned"/> to acknowledge that the <c>DataProtection</c>
    /// container was already provisioned out-of-band (portal, CLI, IaC). Only consulted when no keyed
    /// <see cref="IBlobContainerManager"/> is registered under <paramref name="serviceKey"/> and the keyed storage
    /// requires provisioning; see the <see cref="InvalidOperationException"/> remarks.
    /// </param>
    /// <returns>The <paramref name="builder"/> so that additional calls can be chained.</returns>
    /// <remarks>
    /// Resolves <see cref="IBlobStorage"/> via <c>GetRequiredKeyedService</c> and the container manager via
    /// <c>GetKeyedService</c> under the same key, so a named/keyed store ensures <em>its own</em> container rather than
    /// the unkeyed default. Without this, a keyed store would resolve the unkeyed (or missing) manager and the first
    /// key write/rotation would fail on Azure/S3, since the data plane no longer auto-creates containers.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="serviceKey"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown at first resolution if no keyed <see cref="IBlobContainerManager"/> is registered under
    /// <paramref name="serviceKey"/>, the keyed storage requires container provisioning
    /// (<see cref="IBlobStorage.RequiresContainerProvisioning"/>), and <paramref name="provisioning"/> is not
    /// <see cref="BlobContainerProvisioning.PreProvisioned"/> — the first key write would fail against the missing
    /// container.
    /// </exception>
    public static IDataProtectionBuilder PersistKeysToBlobStorage(
        this IDataProtectionBuilder builder,
        object serviceKey,
        BlobContainerProvisioning provisioning = BlobContainerProvisioning.Managed
    )
    {
        builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(services =>
        {
            var storage = services.GetRequiredKeyedService<IBlobStorage>(serviceKey);
            var containerManager = services.GetKeyedService<IBlobContainerManager>(serviceKey);
            var loggerFactory = services.GetService<ILoggerFactory>();

            _EnsureProvisioningIsCovered(storage, containerManager, provisioning);

            return new ConfigureOptions<KeyManagementOptions>(options =>
                options.XmlRepository = new BlobStorageDataProtectionXmlRepository(
                    storage,
                    containerManager,
                    loggerFactory
                )
            );
        });

        return builder;
    }

    /// <summary>
    /// Configures the data protection system to persist XML key descriptors to an <see cref="IBlobStorage"/> backend
    /// resolved from the application's DI container.
    /// </summary>
    /// <param name="builder">The <see cref="IDataProtectionBuilder"/> to configure.</param>
    /// <param name="provisioning">
    /// Pass <see cref="BlobContainerProvisioning.PreProvisioned"/> to acknowledge that the <c>DataProtection</c>
    /// container was already provisioned out-of-band (portal, CLI, IaC). Only consulted when no unkeyed
    /// <see cref="IBlobContainerManager"/> is registered and the resolved storage requires provisioning; see the
    /// <see cref="InvalidOperationException"/> remarks.
    /// </param>
    /// <returns>The <paramref name="builder"/> so that additional calls can be chained.</returns>
    /// <remarks>
    /// This overload resolves <see cref="IBlobStorage"/> via <c>IServiceProvider.GetRequiredService</c>.
    /// Ensure a concrete <see cref="IBlobStorage"/> registration exists in the DI container; a missing
    /// registration will throw <see cref="InvalidOperationException"/> when the service is first resolved.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown at first resolution if no unkeyed <see cref="IBlobContainerManager"/> is registered, the resolved
    /// storage requires container provisioning (<see cref="IBlobStorage.RequiresContainerProvisioning"/>), and
    /// <paramref name="provisioning"/> is not <see cref="BlobContainerProvisioning.PreProvisioned"/> — the first key
    /// write would fail against the missing container.
    /// </exception>
    public static IDataProtectionBuilder PersistKeysToBlobStorage(
        this IDataProtectionBuilder builder,
        BlobContainerProvisioning provisioning = BlobContainerProvisioning.Managed
    )
    {
        builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(services =>
        {
            var storage = services.GetRequiredService<IBlobStorage>();
            var containerManager = services.GetService<IBlobContainerManager>();
            var loggerFactory = services.GetService<ILoggerFactory>();

            _EnsureProvisioningIsCovered(storage, containerManager, provisioning);

            return new ConfigureOptions<KeyManagementOptions>(options =>
                options.XmlRepository = new BlobStorageDataProtectionXmlRepository(
                    storage,
                    containerManager,
                    loggerFactory
                )
            );
        });

        return builder;
    }

    /// <summary>
    /// Config-time guardrail: a repository whose effective container manager is <see langword="null"/> can never
    /// ensure the <c>DataProtection</c> container, so when the backend also demands a provisioned container the first
    /// key write is guaranteed to fail at runtime. Surface that misconfiguration here — at configuration time for
    /// instance overloads, at first resolution for DI paths — unless the caller explicitly acknowledged out-of-band
    /// provisioning. A present manager (ensures before writes) or a lazy backend (never needs provisioning) passes.
    /// </summary>
    private static void _EnsureProvisioningIsCovered(
        IBlobStorage storage,
        IBlobContainerManager? containerManager,
        BlobContainerProvisioning provisioning
    )
    {
        if (
            containerManager is not null
            || provisioning is BlobContainerProvisioning.PreProvisioned
            || !storage.RequiresContainerProvisioning
        )
        {
            return;
        }

        throw new InvalidOperationException(
            $"PersistKeysToBlobStorage: the '{BlobStorageDataProtectionXmlRepository.ContainerName}' container cannot "
                + $"be ensured because no IBlobContainerManager is available, and the configured storage "
                + $"('{storage.GetType().Name}') requires containers to be provisioned before data-plane writes "
                + "(IBlobStorage.RequiresContainerProvisioning is true) — the first data-protection key write would "
                + "fail. Fix this by (1) wiring an IBlobContainerManager (use the storage+manager overload, or a "
                + "DI/keyed overload that can resolve one), or (2) provisioning the container out-of-band (portal, "
                + "CLI, IaC) and acknowledging it with provisioning: BlobContainerProvisioning.PreProvisioned. For "
                + "providers that ship no container manager — such as Cloudflare R2, whose object-scoped tokens "
                + "cannot create buckets — BlobContainerProvisioning.PreProvisioned is the only option."
        );
    }
}
