// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Serializer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable CA1708 // multiple extension blocks emit marker members differing only by case
namespace Headless.Blobs.FileSystem;

/// <summary>Extension methods to register the file-system blob storage provider.</summary>
[PublicAPI]
public static class SetupFileSystemBlob
{
    extension(HeadlessBlobsSetupBuilder setup)
    {
        /// <summary>Uses the file system as the default (unkeyed) <see cref="IBlobStorage"/>.</summary>
        public HeadlessBlobsSetupBuilder UseFileSystem(Action<FileSystemBlobStorageOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
            {
                services.Configure<FileSystemBlobStorageOptions, FileSystemBlobStorageOptionsValidator>(setupAction);
                services._AddBlobsDefaultCore();
            });

            return setup;
        }

        /// <summary>Uses the file system as the default (unkeyed) <see cref="IBlobStorage"/> with service provider-aware configuration.</summary>
        public HeadlessBlobsSetupBuilder UseFileSystem(
            Action<FileSystemBlobStorageOptions, IServiceProvider> setupAction
        )
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
            {
                services.Configure<FileSystemBlobStorageOptions, FileSystemBlobStorageOptionsValidator>(setupAction);
                services._AddBlobsDefaultCore();
            });

            return setup;
        }

        /// <summary>Uses the file system as the default (unkeyed) <see cref="IBlobStorage"/>, binding options from configuration.</summary>
        public HeadlessBlobsSetupBuilder UseFileSystem(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterDefaultProvider(services =>
            {
                services.Configure<FileSystemBlobStorageOptions, FileSystemBlobStorageOptionsValidator>(configuration);
                services._AddBlobsDefaultCore();
            });

            return setup;
        }
    }

    extension(HeadlessBlobInstanceBuilder instance)
    {
        /// <summary>Uses the file system for this named instance, resolvable as a keyed <see cref="IBlobStorage"/> or through <see cref="IBlobStorageProvider"/>.</summary>
        public HeadlessBlobInstanceBuilder UseFileSystem(Action<FileSystemBlobStorageOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<FileSystemBlobStorageOptions, FileSystemBlobStorageOptionsValidator>(
                    setupAction,
                    name
                );
                services._AddBlobsNamedCore(name);
            });

            return instance;
        }

        /// <summary>Uses the file system for this named instance with service provider-aware configuration.</summary>
        public HeadlessBlobInstanceBuilder UseFileSystem(
            Action<FileSystemBlobStorageOptions, IServiceProvider> setupAction
        )
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<FileSystemBlobStorageOptions, FileSystemBlobStorageOptionsValidator>(
                    setupAction,
                    name
                );
                services._AddBlobsNamedCore(name);
            });

            return instance;
        }

        /// <summary>Uses the file system for this named instance, binding options from configuration.</summary>
        public HeadlessBlobInstanceBuilder UseFileSystem(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<FileSystemBlobStorageOptions, FileSystemBlobStorageOptionsValidator>(
                    configuration,
                    name
                );
                services._AddBlobsNamedCore(name);
            });

            return instance;
        }
    }

    extension(IServiceCollection services)
    {
        private IServiceCollection _AddBlobsDefaultCore()
        {
            services._AddBlobsCoreShared();

            services.AddSingleton<IBlobStorage>(serviceProvider => new FileSystemBlobStorage(
                serviceProvider.GetRequiredService<IOptions<FileSystemBlobStorageOptions>>(),
                serviceProvider.GetRequiredService<IJsonSerializer>(),
                new CrossOsNamingNormalizer(),
                serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System,
                serviceProvider.GetService<ILogger<FileSystemBlobStorage>>()
            ));

            // Container lifecycle is a separately-resolved capability (not a cast from IBlobStorage), registered with
            // its own per-store options + normalizer to mirror the storage's isolation (U12 / KTD5).
            services.AddSingleton<IBlobContainerManager>(serviceProvider => new FileSystemBlobContainerManager(
                serviceProvider.GetRequiredService<IOptions<FileSystemBlobStorageOptions>>(),
                new CrossOsNamingNormalizer()
            ));

            return services;
        }

        private IServiceCollection _AddBlobsNamedCore(string name)
        {
            services._AddBlobsCoreShared();

            services.AddKeyedSingleton<IBlobStorage>(
                name,
                (serviceProvider, _) =>
                    new FileSystemBlobStorage(
                        Options.Create(
                            serviceProvider
                                .GetRequiredService<IOptionsMonitor<FileSystemBlobStorageOptions>>()
                                .Get(name)
                        ),
                        serviceProvider.GetRequiredService<IJsonSerializer>(),
                        new CrossOsNamingNormalizer(),
                        serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System,
                        serviceProvider.GetService<ILogger<FileSystemBlobStorage>>()
                    )
            );

            // Keyed container-management capability for this named instance (separate registration, not a cast).
            services.AddKeyedSingleton<IBlobContainerManager>(
                name,
                (serviceProvider, _) =>
                    new FileSystemBlobContainerManager(
                        Options.Create(
                            serviceProvider
                                .GetRequiredService<IOptionsMonitor<FileSystemBlobStorageOptions>>()
                                .Get(name)
                        ),
                        new CrossOsNamingNormalizer()
                    )
            );

            return services;
        }

        private IServiceCollection _AddBlobsCoreShared()
        {
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IJsonOptionsProvider>(new DefaultJsonOptionsProvider());
            services.TryAddSingleton<IJsonSerializer>(serviceProvider => new SystemJsonSerializer(
                serviceProvider.GetRequiredService<IJsonOptionsProvider>()
            ));

            return services;
        }
    }
}
