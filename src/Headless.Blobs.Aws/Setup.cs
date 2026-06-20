// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Extensions.NETCore.Setup;
using Amazon.S3;
using Headless.Abstractions;
using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable CA1708 // multiple extension blocks emit marker members differing only by case
namespace Headless.Blobs.Aws;

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
                services._AddAwsDefaultCore(awsOptions);
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
                services._AddAwsDefaultCore(awsOptions);
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
                services._AddAwsDefaultCore(awsOptions);
            });

            return setup;
        }
    }

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
                services._AddAwsNamedCore(name, awsOptions);
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
                services._AddAwsNamedCore(name, awsOptions);
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
                services._AddAwsNamedCore(name, awsOptions);
            });

            return instance;
        }
    }

    extension(IServiceCollection services)
    {
        private IServiceCollection _AddAwsDefaultCore(AWSOptions? awsOptions)
        {
            services.AddBlobStorageProvider();

            services.AddSingleton<IBlobStorage>(serviceProvider => new AwsBlobStorage(
                S3ClientFactory.Create(awsOptions),
                serviceProvider.GetRequiredService<IMimeTypeProvider>(),
                serviceProvider.GetRequiredService<IClock>(),
                serviceProvider.GetRequiredService<IOptions<AwsBlobStorageOptions>>(),
                new AwsBlobNamingNormalizer(),
                serviceProvider.GetService<ILogger<AwsBlobStorage>>()
            ));

            services.AddSingleton<IPresignedUrlBlobStorage>(serviceProvider =>
                (IPresignedUrlBlobStorage)serviceProvider.GetRequiredService<IBlobStorage>()
            );

            return services;
        }

        private IServiceCollection _AddAwsNamedCore(string name, AWSOptions? awsOptions)
        {
            services.AddBlobStorageProvider();

            services.AddKeyedSingleton<IBlobStorage>(
                name,
                (serviceProvider, _) =>
                    new AwsBlobStorage(
                        S3ClientFactory.Create(awsOptions),
                        serviceProvider.GetRequiredService<IMimeTypeProvider>(),
                        serviceProvider.GetRequiredService<IClock>(),
                        Options.Create(
                            serviceProvider.GetRequiredService<IOptionsMonitor<AwsBlobStorageOptions>>().Get(name)
                        ),
                        new AwsBlobNamingNormalizer(),
                        serviceProvider.GetService<ILogger<AwsBlobStorage>>()
                    )
            );

            services.AddKeyedSingleton<IPresignedUrlBlobStorage>(
                name,
                (serviceProvider, _) =>
                    (IPresignedUrlBlobStorage)serviceProvider.GetRequiredKeyedService<IBlobStorage>(name)
            );

            return services;
        }
    }
}
