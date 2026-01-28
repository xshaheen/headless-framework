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
    /// <summary>Configures the data protection system to persist keys to file storage.</summary>
    /// <param name="builder">The builder instance to modify.</param>
    /// <param name="storage">The storage account to use.</param>
    /// <param name="loggerFactory">The logger factory to use.</param>
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

    /// <summary>Configures the data protection system to persist keys to file storage.</summary>
    /// <param name="builder">The builder instance to modify.</param>
    /// <param name="storageFactory">The storage factory to use.</param>
    public static IDataProtectionBuilder PersistKeysToBlobStorage(
        this IDataProtectionBuilder builder,
        Func<IServiceProvider, IBlobStorage> storageFactory
    )
    {
        builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(services =>
        {
            var storage = storageFactory.Invoke(services);
            var loggerFactory = services.GetService<ILoggerFactory>();

            return new ConfigureOptions<KeyManagementOptions>(options =>
                options.XmlRepository = new BlobStorageDataProtectionXmlRepository(storage, loggerFactory)
            );
        });

        return builder;
    }

    /// <summary>Configures the data protection system to persist keys to file storage.</summary>
    public static IDataProtectionBuilder PersistKeysToBlobStorage(this IDataProtectionBuilder builder)
    {
        builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(services =>
        {
            var storage = services.GetRequiredService<IBlobStorage>();
            var loggerFactory = services.GetService<ILoggerFactory>();

            return new ConfigureOptions<KeyManagementOptions>(options =>
                options.XmlRepository = new BlobStorageDataProtectionXmlRepository(storage, loggerFactory)
            );
        });

        return builder;
    }
}
