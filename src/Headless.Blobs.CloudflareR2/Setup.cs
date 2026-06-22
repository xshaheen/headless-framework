// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Blobs.Aws;
using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

#pragma warning disable CA1708 // multiple extension blocks emit marker members differing only by case
namespace Headless.Blobs.CloudflareR2;

/// <summary>Extension methods to register the Cloudflare R2 blob storage provider.</summary>
[PublicAPI]
public static class SetupCloudflareR2Blob
{
    extension(HeadlessBlobsSetupBuilder setup)
    {
        /// <summary>Uses Cloudflare R2 as the default (unkeyed) <see cref="IBlobStorage"/>, reusing the S3 engine behind an R2-tuned client.</summary>
        public HeadlessBlobsSetupBuilder UseCloudflareR2(Action<R2BlobStorageOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
            {
                services.Configure<R2BlobStorageOptions, R2BlobStorageOptionsValidator>(setupAction);
                services._AddBlobsDefaultCore();
            });

            return setup;
        }

        /// <summary>Uses Cloudflare R2 as the default (unkeyed) <see cref="IBlobStorage"/> with service provider-aware configuration.</summary>
        public HeadlessBlobsSetupBuilder UseCloudflareR2(Action<R2BlobStorageOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
            {
                services.Configure<R2BlobStorageOptions, R2BlobStorageOptionsValidator>(setupAction);
                services._AddBlobsDefaultCore();
            });

            return setup;
        }

        /// <summary>Uses Cloudflare R2 as the default (unkeyed) <see cref="IBlobStorage"/>, binding options from configuration.</summary>
        public HeadlessBlobsSetupBuilder UseCloudflareR2(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            setup.RegisterDefaultProvider(services =>
            {
                services.Configure<R2BlobStorageOptions, R2BlobStorageOptionsValidator>(configuration);
                services._AddBlobsDefaultCore();
            });

            return setup;
        }
    }

    extension(HeadlessBlobInstanceBuilder instance)
    {
        /// <summary>Uses Cloudflare R2 for this named instance, resolvable as a keyed <see cref="IBlobStorage"/> or through <see cref="IBlobStorageProvider"/>.</summary>
        public HeadlessBlobInstanceBuilder UseCloudflareR2(Action<R2BlobStorageOptions> setupAction)
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<R2BlobStorageOptions, R2BlobStorageOptionsValidator>(setupAction, name);
                services._AddBlobsNamedCore(name);
            });

            return instance;
        }

        /// <summary>Uses Cloudflare R2 for this named instance with service provider-aware configuration.</summary>
        public HeadlessBlobInstanceBuilder UseCloudflareR2(Action<R2BlobStorageOptions, IServiceProvider> setupAction)
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<R2BlobStorageOptions, R2BlobStorageOptionsValidator>(setupAction, name);
                services._AddBlobsNamedCore(name);
            });

            return instance;
        }

        /// <summary>Uses Cloudflare R2 for this named instance, binding options from configuration.</summary>
        public HeadlessBlobInstanceBuilder UseCloudflareR2(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);

            var name = instance.Name;

            instance.RegisterProvider(services =>
            {
                services.Configure<R2BlobStorageOptions, R2BlobStorageOptionsValidator>(configuration, name);
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

            services.AddSingleton<IBlobStorage>(serviceProvider =>
            {
                var r2Options = serviceProvider.GetRequiredService<IOptions<R2BlobStorageOptions>>();
                _ = r2Options.Value;
                var mimeTypeProvider = serviceProvider.GetRequiredService<IMimeTypeProvider>();
                var clock = serviceProvider.GetRequiredService<IClock>();
                var awsOptions = new AwsBlobStorageOptions();
                _ApplyR2ForcedDefaults(awsOptions);
                var logger =
                    serviceProvider.GetService<ILogger<AwsBlobStorage>>() ?? NullLogger<AwsBlobStorage>.Instance;
                var s3Client = R2ClientFactory.Create(r2Options.Value);

                return new AwsBlobStorage(
                    s3Client,
                    mimeTypeProvider,
                    clock,
                    Options.Create(awsOptions),
                    new R2BlobNamingNormalizer(),
                    logger
                );
            });

            return services;
        }

        private IServiceCollection _AddBlobsNamedCore(string name)
        {
            services.AddBlobStorageProvider();

            // R2-safe behavior bound per-instance (named AwsBlobStorageOptions) so coexisting AWS stores are
            // never affected by R2's forced defaults.
            services.Configure<AwsBlobStorageOptions>(name, _ApplyR2ForcedDefaults);

            services.AddKeyedSingleton<IBlobStorage>(
                name,
                (serviceProvider, _) =>
                    new AwsBlobStorage(
                        R2ClientFactory.Create(
                            serviceProvider.GetRequiredService<IOptionsMonitor<R2BlobStorageOptions>>().Get(name)
                        ),
                        serviceProvider.GetRequiredService<IMimeTypeProvider>(),
                        serviceProvider.GetRequiredService<IClock>(),
                        Options.Create(
                            serviceProvider.GetRequiredService<IOptionsMonitor<AwsBlobStorageOptions>>().Get(name)
                        ),
                        new R2BlobNamingNormalizer(),
                        serviceProvider.GetService<ILogger<AwsBlobStorage>>() ?? NullLogger<AwsBlobStorage>.Instance
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

    private static void _ApplyR2ForcedDefaults(AwsBlobStorageOptions options)
    {
        // R2 has no ACLs, rejects chunked/payload signing, and object-scoped tokens cannot create buckets.
        options.CannedAcl = null;
        options.UseChunkEncoding = false;
        options.DisablePayloadSigning = true;
        options.AutoCreateContainer = false;
    }
}
