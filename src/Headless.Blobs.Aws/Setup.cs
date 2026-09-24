// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Extensions.NETCore.Setup;
using Headless.Abstractions;
using Headless.Blobs.Aws;
using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Blobs;

/// <summary>Extension methods to register the AWS S3 blob storage provider.</summary>
[PublicAPI]
public static class SetupAwsS3
{
    extension(HeadlessBlobsSetupBuilder setup)
    {
        /// <summary>
        /// Uses AWS S3 as the default (unkeyed) <see cref="IBlobStorage"/>. The SDK resolves credentials and
        /// region through the standard AWS credential chain unless an explicit <paramref name="awsOptions"/> is
        /// supplied.
        /// </summary>
        /// <param name="setupAction">Configures S3 behavior options (ACL, chunk encoding, etc.).</param>
        /// <param name="awsOptions">
        /// Optional per-store AWS SDK options (region, credentials, endpoint). When <see langword="null"/> the
        /// SDK credential and region chain applies.
        /// </param>
        public HeadlessBlobsSetupBuilder UseAws(
            Action<AwsBlobStorageOptions> setupAction,
            AWSOptions? awsOptions = null
        )
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
            {
                services.Configure<AwsBlobStorageOptions, AwsBlobStorageOptionsValidator>(setupAction);
                _AddBlobsDefaultCore(services, awsOptions);
            });

            return setup;
        }

        /// <summary>
        /// Uses AWS S3 as the default (unkeyed) <see cref="IBlobStorage"/> with service provider-aware
        /// configuration.
        /// </summary>
        /// <param name="setupAction">Configures S3 behavior options using the service provider.</param>
        /// <param name="awsOptions">
        /// Optional per-store AWS SDK options (region, credentials, endpoint). When <see langword="null"/> the
        /// SDK credential and region chain applies.
        /// </param>
        public HeadlessBlobsSetupBuilder UseAws(
            Action<AwsBlobStorageOptions, IServiceProvider> setupAction,
            AWSOptions? awsOptions = null
        )
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
            {
                services.Configure<AwsBlobStorageOptions, AwsBlobStorageOptionsValidator>(setupAction);
                _AddBlobsDefaultCore(services, awsOptions);
            });

            return setup;
        }

        /// <summary>
        /// Uses AWS S3 as the default (unkeyed) <see cref="IBlobStorage"/>, binding behavior options from
        /// configuration.
        /// </summary>
        /// <param name="configuration">The configuration section to bind <see cref="AwsBlobStorageOptions"/> from.</param>
        /// <param name="awsOptions">
        /// Optional per-store AWS SDK options (region, credentials, endpoint). When <see langword="null"/> the
        /// SDK credential and region chain applies.
        /// </param>
        public HeadlessBlobsSetupBuilder UseAws(IConfiguration configuration, AWSOptions? awsOptions = null)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterDefaultProvider(services =>
            {
                services.Configure<AwsBlobStorageOptions, AwsBlobStorageOptionsValidator>(configuration);
                _AddBlobsDefaultCore(services, awsOptions);
            });

            return setup;
        }

        /// <summary>
        /// Uses an S3-compatible endpoint (MinIO, Ceph RGW, Garage, and similar) as the default (unkeyed)
        /// <see cref="IBlobStorage"/>, with path-style addressing and the SDK checksum, ACL, and payload-signing
        /// behavior those servers accept.
        /// </summary>
        /// <param name="serviceUrl">Absolute endpoint URL. A plaintext <c>http://</c> URL also needs <see cref="S3CompatibleBlobStorageOptions.AllowInsecureHttp"/>.</param>
        /// <param name="accessKeyId">The access key id.</param>
        /// <param name="secretAccessKey">The secret access key.</param>
        /// <param name="configure">Optionally adjusts the remaining options, such as <see cref="S3CompatibleBlobStorageOptions.AllowInsecureHttp"/>.</param>
        public HeadlessBlobsSetupBuilder UseS3Compatible(
            string serviceUrl,
            string accessKeyId,
            string secretAccessKey,
            Action<S3CompatibleBlobStorageOptions>? configure = null
        )
        {
            return setup.UseS3Compatible(S3CompatibleSetup(serviceUrl, accessKeyId, secretAccessKey, configure));
        }

        /// <summary>Uses an S3-compatible endpoint as the default (unkeyed) <see cref="IBlobStorage"/>.</summary>
        /// <param name="setupAction">Configures the endpoint, credentials, and options.</param>
        public HeadlessBlobsSetupBuilder UseS3Compatible(Action<S3CompatibleBlobStorageOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
            {
                services.Configure<S3CompatibleBlobStorageOptions, S3CompatibleBlobStorageOptionsValidator>(
                    setupAction
                );
                _AddS3CompatibleDefaultCore(services);
            });

            return setup;
        }

        /// <summary>
        /// Uses an S3-compatible endpoint as the default (unkeyed) <see cref="IBlobStorage"/> with service
        /// provider-aware configuration.
        /// </summary>
        /// <param name="setupAction">Configures the endpoint, credentials, and options using the service provider.</param>
        public HeadlessBlobsSetupBuilder UseS3Compatible(
            Action<S3CompatibleBlobStorageOptions, IServiceProvider> setupAction
        )
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
            {
                services.Configure<S3CompatibleBlobStorageOptions, S3CompatibleBlobStorageOptionsValidator>(
                    setupAction
                );
                _AddS3CompatibleDefaultCore(services);
            });

            return setup;
        }

        /// <summary>
        /// Uses an S3-compatible endpoint as the default (unkeyed) <see cref="IBlobStorage"/>, binding
        /// <see cref="S3CompatibleBlobStorageOptions"/> from configuration.
        /// </summary>
        /// <param name="configuration">The configuration section to bind <see cref="S3CompatibleBlobStorageOptions"/> from.</param>
        public HeadlessBlobsSetupBuilder UseS3Compatible(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterDefaultProvider(services =>
            {
                services.Configure<S3CompatibleBlobStorageOptions, S3CompatibleBlobStorageOptionsValidator>(
                    configuration
                );
                _AddS3CompatibleDefaultCore(services);
            });

            return setup;
        }
    }

    internal static Action<S3CompatibleBlobStorageOptions> S3CompatibleSetup(
        string serviceUrl,
        string accessKeyId,
        string secretAccessKey,
        Action<S3CompatibleBlobStorageOptions>? configure
    )
    {
        Argument.IsNotNullOrWhiteSpace(serviceUrl);
        Argument.IsNotNullOrWhiteSpace(accessKeyId);
        Argument.IsNotNullOrWhiteSpace(secretAccessKey);

        return options =>
        {
            options.ServiceUrl = serviceUrl;
            options.AccessKeyId = accessKeyId;
            options.SecretAccessKey = secretAccessKey;
            configure?.Invoke(options);
        };
    }

    private static IServiceCollection _AddS3CompatibleDefaultCore(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IBlobStorage>(serviceProvider =>
            _CreateS3CompatibleStorage(
                serviceProvider,
                serviceProvider.GetRequiredService<IOptions<S3CompatibleBlobStorageOptions>>().Value
            )
        );

        // S3-compatible servers generally support bucket create/delete, so the capability is registered like AWS.
        services.AddSingleton<IBlobContainerManager>(serviceProvider => new AwsBlobContainerManager(
            S3ClientFactory.CreateS3Compatible(
                serviceProvider.GetRequiredService<IOptions<S3CompatibleBlobStorageOptions>>().Value
            ),
            new AwsBlobNamingNormalizer()
        ));

        return services;
    }

    internal static IServiceCollection AddS3CompatibleNamedCore(IServiceCollection services, string name)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddKeyedSingleton<IBlobStorage>(
            name,
            (serviceProvider, _) =>
                _CreateS3CompatibleStorage(
                    serviceProvider,
                    serviceProvider.GetRequiredService<IOptionsMonitor<S3CompatibleBlobStorageOptions>>().Get(name)
                )
        );

        services.AddKeyedSingleton<IPresignedUrlBlobStorage>(
            name,
            (serviceProvider, _) =>
                (IPresignedUrlBlobStorage)serviceProvider.GetRequiredKeyedService<IBlobStorage>(name)
        );

        services.AddKeyedSingleton<IBlobContainerManager>(
            name,
            (serviceProvider, _) =>
                new AwsBlobContainerManager(
                    S3ClientFactory.CreateS3Compatible(
                        serviceProvider.GetRequiredService<IOptionsMonitor<S3CompatibleBlobStorageOptions>>().Get(name)
                    ),
                    new AwsBlobNamingNormalizer()
                )
        );

        return services;
    }

    private static AwsBlobStorage _CreateS3CompatibleStorage(
        IServiceProvider serviceProvider,
        S3CompatibleBlobStorageOptions options
    )
    {
        var storageOptions = new AwsBlobStorageOptions();
        S3ClientFactory.ApplyS3CompatibleDefaults(storageOptions, options);
        var mimeTypeProvider = serviceProvider.GetRequiredService<IMimeTypeProvider>();
        var timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
        var logger = serviceProvider.GetService<ILogger<AwsBlobStorage>>() ?? NullLogger<AwsBlobStorage>.Instance;
        var wrappedOptions = Options.Create(storageOptions);
        var normalizer = new AwsBlobNamingNormalizer();
#pragma warning disable CA2000 // False positive: ownership transfers to AwsBlobStorage, which disposes the client.
        var s3Client = S3ClientFactory.CreateS3Compatible(options);
#pragma warning restore CA2000

        return new AwsBlobStorage(s3Client, mimeTypeProvider, timeProvider, wrappedOptions, normalizer, logger);
    }

    private static IServiceCollection _AddBlobsDefaultCore(IServiceCollection services, AWSOptions? awsOptions)
    {
        // Defensive: this package RESOLVES TimeProvider, so it must also guarantee one exists. Without this,
        // installing the package standalone (no ServiceDefaults, no sibling that happens to register it) throws
        // 'No service for type TimeProvider' at resolve time.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IBlobStorage>(serviceProvider =>
        {
            var mimeTypeProvider = serviceProvider.GetRequiredService<IMimeTypeProvider>();
            var timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
            var options = serviceProvider.GetRequiredService<IOptions<AwsBlobStorageOptions>>();
            var logger = serviceProvider.GetService<ILogger<AwsBlobStorage>>() ?? NullLogger<AwsBlobStorage>.Instance;
            var s3Client = S3ClientFactory.Create(awsOptions);

            return new AwsBlobStorage(
                s3Client,
                mimeTypeProvider,
                timeProvider,
                options,
                new AwsBlobNamingNormalizer(),
                logger
            );
        });

        // Container lifecycle is a separately-resolved capability (not a cast from IBlobStorage), so the AWS
        // provider registers a dedicated manager with its own per-store S3 client. Cloudflare R2 reuses
        // AwsBlobStorage but registers no manager, so its IBlobContainerManager resolves to null.
        services.AddSingleton<IBlobContainerManager>(_ => new AwsBlobContainerManager(
            S3ClientFactory.Create(awsOptions),
            new AwsBlobNamingNormalizer()
        ));

        return services;
    }

    internal static IServiceCollection AddBlobsNamedCore(
        IServiceCollection services,
        string name,
        AWSOptions? awsOptions
    )
    {
        services.AddKeyedSingleton<IBlobStorage>(
            name,
            (serviceProvider, _) =>
            {
                var mimeTypeProvider = serviceProvider.GetRequiredService<IMimeTypeProvider>();
                var timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
                var options = Options.Create(
                    serviceProvider.GetRequiredService<IOptionsMonitor<AwsBlobStorageOptions>>().Get(name)
                );
                var logger =
                    serviceProvider.GetService<ILogger<AwsBlobStorage>>() ?? NullLogger<AwsBlobStorage>.Instance;
                var s3Client = S3ClientFactory.Create(awsOptions);

                return new AwsBlobStorage(
                    s3Client,
                    mimeTypeProvider,
                    timeProvider,
                    options,
                    new AwsBlobNamingNormalizer(),
                    logger
                );
            }
        );

        services.AddKeyedSingleton<IPresignedUrlBlobStorage>(
            name,
            (serviceProvider, _) =>
                (IPresignedUrlBlobStorage)serviceProvider.GetRequiredKeyedService<IBlobStorage>(name)
        );

        // Keyed container-management capability for this named instance, registered with its own per-store S3
        // client (per-instance isolation). This is a separate registration, not a cast from the keyed storage,
        // so providers that share AwsBlobStorage but cannot manage buckets (Cloudflare R2) simply omit it.
        services.AddKeyedSingleton<IBlobContainerManager>(
            name,
            (_, _) => new AwsBlobContainerManager(S3ClientFactory.Create(awsOptions), new AwsBlobNamingNormalizer())
        );

        return services;
    }
}

/// <summary>Extension methods to register the AWS S3 blob storage provider as a named store.</summary>
[PublicAPI]
public static class SetupAwsS3Named
{
    extension(HeadlessBlobInstanceBuilder instance)
    {
        /// <summary>
        /// Uses AWS S3 for this named instance, resolvable as a keyed <see cref="IBlobStorage"/> or through
        /// <see cref="IBlobStorageProvider"/>.
        /// </summary>
        /// <param name="setupAction">Configures S3 behavior options (ACL, chunk encoding, etc.).</param>
        /// <param name="awsOptions">
        /// Optional per-store AWS SDK options (region, credentials, endpoint). When <see langword="null"/> the
        /// SDK credential and region chain applies.
        /// </param>
        public HeadlessBlobInstanceBuilder UseAws(
            Action<AwsBlobStorageOptions> setupAction,
            AWSOptions? awsOptions = null
        )
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<AwsBlobStorageOptions, AwsBlobStorageOptionsValidator>(setupAction, name);
                SetupAwsS3.AddBlobsNamedCore(services, name, awsOptions);
            });

            return instance;
        }

        /// <summary>
        /// Uses AWS S3 for this named instance with service provider-aware configuration.
        /// </summary>
        /// <param name="setupAction">Configures S3 behavior options using the service provider.</param>
        /// <param name="awsOptions">
        /// Optional per-store AWS SDK options (region, credentials, endpoint). When <see langword="null"/> the
        /// SDK credential and region chain applies.
        /// </param>
        public HeadlessBlobInstanceBuilder UseAws(
            Action<AwsBlobStorageOptions, IServiceProvider> setupAction,
            AWSOptions? awsOptions = null
        )
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<AwsBlobStorageOptions, AwsBlobStorageOptionsValidator>(setupAction, name);
                SetupAwsS3.AddBlobsNamedCore(services, name, awsOptions);
            });

            return instance;
        }

        /// <summary>
        /// Uses AWS S3 for this named instance, binding behavior options from configuration.
        /// </summary>
        /// <param name="configuration">The configuration section to bind <see cref="AwsBlobStorageOptions"/> from.</param>
        /// <param name="awsOptions">
        /// Optional per-store AWS SDK options (region, credentials, endpoint). When <see langword="null"/> the
        /// SDK credential and region chain applies.
        /// </param>
        public HeadlessBlobInstanceBuilder UseAws(IConfiguration configuration, AWSOptions? awsOptions = null)
        {
            Argument.IsNotNull(configuration);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<AwsBlobStorageOptions, AwsBlobStorageOptionsValidator>(configuration, name);
                SetupAwsS3.AddBlobsNamedCore(services, name, awsOptions);
            });

            return instance;
        }

        /// <summary>
        /// Uses an S3-compatible endpoint (MinIO, Ceph RGW, Garage, and similar) for this named instance,
        /// resolvable as a keyed <see cref="IBlobStorage"/> or through <see cref="IBlobStorageProvider"/>.
        /// </summary>
        /// <param name="serviceUrl">Absolute endpoint URL. A plaintext <c>http://</c> URL also needs <see cref="S3CompatibleBlobStorageOptions.AllowInsecureHttp"/>.</param>
        /// <param name="accessKeyId">The access key id.</param>
        /// <param name="secretAccessKey">The secret access key.</param>
        /// <param name="configure">Optionally adjusts the remaining options, such as <see cref="S3CompatibleBlobStorageOptions.AllowInsecureHttp"/>.</param>
        public HeadlessBlobInstanceBuilder UseS3Compatible(
            string serviceUrl,
            string accessKeyId,
            string secretAccessKey,
            Action<S3CompatibleBlobStorageOptions>? configure = null
        )
        {
            return instance.UseS3Compatible(
                SetupAwsS3.S3CompatibleSetup(serviceUrl, accessKeyId, secretAccessKey, configure)
            );
        }

        /// <summary>Uses an S3-compatible endpoint for this named instance.</summary>
        /// <param name="setupAction">Configures the endpoint, credentials, and options.</param>
        public HeadlessBlobInstanceBuilder UseS3Compatible(Action<S3CompatibleBlobStorageOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<S3CompatibleBlobStorageOptions, S3CompatibleBlobStorageOptionsValidator>(
                    setupAction,
                    name
                );
                SetupAwsS3.AddS3CompatibleNamedCore(services, name);
            });

            return instance;
        }

        /// <summary>Uses an S3-compatible endpoint for this named instance with service provider-aware configuration.</summary>
        /// <param name="setupAction">Configures the endpoint, credentials, and options using the service provider.</param>
        public HeadlessBlobInstanceBuilder UseS3Compatible(
            Action<S3CompatibleBlobStorageOptions, IServiceProvider> setupAction
        )
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<S3CompatibleBlobStorageOptions, S3CompatibleBlobStorageOptionsValidator>(
                    setupAction,
                    name
                );
                SetupAwsS3.AddS3CompatibleNamedCore(services, name);
            });

            return instance;
        }

        /// <summary>
        /// Uses an S3-compatible endpoint for this named instance, binding
        /// <see cref="S3CompatibleBlobStorageOptions"/> from configuration.
        /// </summary>
        /// <param name="configuration">The configuration section to bind <see cref="S3CompatibleBlobStorageOptions"/> from.</param>
        public HeadlessBlobInstanceBuilder UseS3Compatible(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<S3CompatibleBlobStorageOptions, S3CompatibleBlobStorageOptionsValidator>(
                    configuration,
                    name
                );
                SetupAwsS3.AddS3CompatibleNamedCore(services, name);
            });

            return instance;
        }
    }
}
