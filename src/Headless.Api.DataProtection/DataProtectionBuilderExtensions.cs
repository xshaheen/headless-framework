// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
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
    /// <returns>The <paramref name="builder"/> so that additional calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="storage"/> is <see langword="null"/>.
    /// </exception>
    public static IDataProtectionBuilder PersistKeysToBlobStorage(
        this IDataProtectionBuilder builder,
        IBlobStorage storage,
        ILoggerFactory? loggerFactory = null
    )
    {
        builder.Services.Configure<KeyManagementOptions>(options =>
        {
            options.XmlRepository = new BlobStorageDataProtectionXmlRepository(storage, loggerFactory);
        });

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
    /// <returns>The <paramref name="builder"/> so that additional calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="storageFactory"/> is <see langword="null"/>.
    /// </exception>
    public static IDataProtectionBuilder PersistKeysToBlobStorage(
        this IDataProtectionBuilder builder,
        Func<IServiceProvider, IBlobStorage> storageFactory,
        Func<IServiceProvider, IBlobContainerManager?>? containerManagerFactory = null
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
    public static IDataProtectionBuilder PersistKeysToBlobStorage(
        this IDataProtectionBuilder builder,
        object serviceKey
    )
    {
        builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(services =>
        {
            var storage = services.GetRequiredKeyedService<IBlobStorage>(serviceKey);
            var containerManager = services.GetKeyedService<IBlobContainerManager>(serviceKey);
            var loggerFactory = services.GetService<ILoggerFactory>();

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
    /// <returns>The <paramref name="builder"/> so that additional calls can be chained.</returns>
    /// <remarks>
    /// This overload resolves <see cref="IBlobStorage"/> via <c>IServiceProvider.GetRequiredService</c>.
    /// Ensure a concrete <see cref="IBlobStorage"/> registration exists in the DI container; a missing
    /// registration will throw <see cref="InvalidOperationException"/> when the service is first resolved.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IDataProtectionBuilder PersistKeysToBlobStorage(this IDataProtectionBuilder builder)
    {
        builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(services =>
        {
            var storage = services.GetRequiredService<IBlobStorage>();
            var containerManager = services.GetService<IBlobContainerManager>();
            var loggerFactory = services.GetService<ILoggerFactory>();

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
}
