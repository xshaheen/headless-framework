// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Extensions.NETCore.Setup;
using Headless.Abstractions;
using Headless.Blobs.Aws;
using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    }

    private static IServiceCollection _AddBlobsDefaultCore(IServiceCollection services, AWSOptions? awsOptions)
    {
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
        // AwsBlobStorage but registers no manager, so its IBlobContainerManager resolves to null (U12 / KTD5).
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
    }
}
