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
    /// <returns>The <paramref name="builder"/> so that additional calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="storageFactory"/> is <see langword="null"/>.
    /// </exception>
    public static IDataProtectionBuilder PersistKeysToBlobStorage(
        this IDataProtectionBuilder builder,
        Func<IServiceProvider, IBlobStorage> storageFactory
    )
    {
        builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(services =>
        {
            var storage = storageFactory.Invoke(services);
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
