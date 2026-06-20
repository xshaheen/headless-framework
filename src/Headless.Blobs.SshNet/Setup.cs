// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

#pragma warning disable CA1708 // multiple extension blocks emit marker members differing only by case
namespace Headless.Blobs.SshNet;

[PublicAPI]
public static class SetupSsh
{
    extension(HeadlessBlobsSetupBuilder setup)
    {
        /// <summary>Uses SSH/SFTP as the default (unkeyed) <see cref="IBlobStorage"/>.</summary>
        public HeadlessBlobsSetupBuilder UseSsh(Action<SshBlobStorageOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
            {
                services.Configure<SshBlobStorageOptions, SshBlobStorageOptionsValidator>(setupAction);
                services._AddBlobsDefaultCore();
            });

            return setup;
        }

        /// <summary>Uses SSH/SFTP as the default (unkeyed) <see cref="IBlobStorage"/> with service provider-aware configuration.</summary>
        public HeadlessBlobsSetupBuilder UseSsh(Action<SshBlobStorageOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
            {
                services.Configure<SshBlobStorageOptions, SshBlobStorageOptionsValidator>(setupAction);
                services._AddBlobsDefaultCore();
            });

            return setup;
        }

        /// <summary>Uses SSH/SFTP as the default (unkeyed) <see cref="IBlobStorage"/>, binding options from configuration.</summary>
        public HeadlessBlobsSetupBuilder UseSsh(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterDefaultProvider(services =>
            {
                services.Configure<SshBlobStorageOptions, SshBlobStorageOptionsValidator>(configuration);
                services._AddBlobsDefaultCore();
            });

            return setup;
        }
    }

    extension(HeadlessBlobInstanceBuilder instance)
    {
        /// <summary>Uses SSH/SFTP for this named instance, resolvable as a keyed <see cref="IBlobStorage"/> or through <see cref="IBlobStorageProvider"/>.</summary>
        public HeadlessBlobInstanceBuilder UseSsh(Action<SshBlobStorageOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<SshBlobStorageOptions, SshBlobStorageOptionsValidator>(setupAction, name);
                services._AddBlobsNamedCore(name);
            });

            return instance;
        }

        /// <summary>Uses SSH/SFTP for this named instance with service provider-aware configuration.</summary>
        public HeadlessBlobInstanceBuilder UseSsh(Action<SshBlobStorageOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<SshBlobStorageOptions, SshBlobStorageOptionsValidator>(setupAction, name);
                services._AddBlobsNamedCore(name);
            });

            return instance;
        }

        /// <summary>Uses SSH/SFTP for this named instance, binding options from configuration.</summary>
        public HeadlessBlobInstanceBuilder UseSsh(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<SshBlobStorageOptions, SshBlobStorageOptionsValidator>(configuration, name);
                services._AddBlobsNamedCore(name);
            });

            return instance;
        }
    }

    extension(IServiceCollection services)
    {
        private IServiceCollection _AddBlobsDefaultCore()
        {
            services.AddBlobStorageProvider();

            // Pool is registered as a DI singleton so the container owns its disposal. Built via a factory so a
            // missing ILogger (host without AddLogging) falls back to NullLogger instead of failing activation.
            services.AddSingleton(serviceProvider => new SftpClientPool(
                serviceProvider.GetRequiredService<IOptions<SshBlobStorageOptions>>(),
                serviceProvider.GetService<ILogger<SftpClientPool>>() ?? NullLogger<SftpClientPool>.Instance
            ));

            services.AddSingleton<IBlobStorage>(serviceProvider => new SshBlobStorage(
                serviceProvider.GetRequiredService<SftpClientPool>(),
                new CrossOsNamingNormalizer(),
                serviceProvider.GetRequiredService<IOptionsMonitor<SshBlobStorageOptions>>(),
                serviceProvider.GetService<ILogger<SshBlobStorage>>() ?? NullLogger<SshBlobStorage>.Instance
            ));

            return services;
        }

        private IServiceCollection _AddBlobsNamedCore(string name)
        {
            services.AddBlobStorageProvider();

            // Each named store gets its own pool bound to its named options. The pool is registered as a keyed
            // singleton so the DI container owns its lifecycle and calls Dispose when the provider is disposed.
            services.AddKeyedSingleton<SftpClientPool>(
                name,
                (serviceProvider, _) =>
                {
                    var monitor = serviceProvider.GetRequiredService<IOptionsMonitor<SshBlobStorageOptions>>();
                    var poolLogger =
                        serviceProvider.GetService<ILogger<SftpClientPool>>() ?? NullLogger<SftpClientPool>.Instance;

                    return new SftpClientPool(Options.Create(monitor.Get(name)), poolLogger);
                }
            );

            services.AddKeyedSingleton<IBlobStorage>(
                name,
                (serviceProvider, _) =>
                {
                    var pool = serviceProvider.GetRequiredKeyedService<SftpClientPool>(name);
                    var monitor = serviceProvider.GetRequiredService<IOptionsMonitor<SshBlobStorageOptions>>();
                    // Wrap the named snapshot so the engine's .CurrentValue calls see this instance's options.
                    var namedMonitor = new OptionsMonitorWrapper<SshBlobStorageOptions>(monitor.Get(name));

                    return new SshBlobStorage(
                        pool,
                        new CrossOsNamingNormalizer(),
                        namedMonitor,
                        serviceProvider.GetService<ILogger<SshBlobStorage>>() ?? NullLogger<SshBlobStorage>.Instance
                    );
                }
            );

            return services;
        }
    }
}
