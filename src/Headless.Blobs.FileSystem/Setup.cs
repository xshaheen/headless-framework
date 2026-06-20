// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable CA1708 // multiple extension blocks emit marker members differing only by case
namespace Headless.Blobs.FileSystem;

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
                services._AddFileSystemDefaultCore();
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
                services._AddFileSystemDefaultCore();
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
                services._AddFileSystemDefaultCore();
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
                services._AddFileSystemNamedCore(name);
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
                services._AddFileSystemNamedCore(name);
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
                services._AddFileSystemNamedCore(name);
            });

            return instance;
        }
    }

    extension(IServiceCollection services)
    {
        private IServiceCollection _AddFileSystemDefaultCore()
        {
            services.AddBlobStorageProvider();

            services.AddSingleton<IBlobStorage>(serviceProvider => new FileSystemBlobStorage(
                serviceProvider.GetRequiredService<IOptions<FileSystemBlobStorageOptions>>(),
                new CrossOsNamingNormalizer(),
                serviceProvider.GetRequiredService<ILogger<FileSystemBlobStorage>>()
            ));

            return services;
        }

        private IServiceCollection _AddFileSystemNamedCore(string name)
        {
            services.AddBlobStorageProvider();

            services.AddKeyedSingleton<IBlobStorage>(
                name,
                (serviceProvider, _) =>
                    new FileSystemBlobStorage(
                        Options.Create(
                            serviceProvider
                                .GetRequiredService<IOptionsMonitor<FileSystemBlobStorageOptions>>()
                                .Get(name)
                        ),
                        new CrossOsNamingNormalizer(),
                        serviceProvider.GetRequiredService<ILogger<FileSystemBlobStorage>>()
                    )
            );

            return services;
        }
    }
}
