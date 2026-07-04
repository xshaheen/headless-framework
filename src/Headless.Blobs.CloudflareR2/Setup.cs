// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Blobs.Aws;
using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
                _AddBlobsDefaultCore(services);
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
                _AddBlobsDefaultCore(services);
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
                _AddBlobsDefaultCore(services);
            });

            return setup;
        }
    }

    private static IServiceCollection _AddBlobsDefaultCore(IServiceCollection services)
    {
        services.AddSingleton<IBlobStorage>(serviceProvider =>
        {
            var r2Options = serviceProvider.GetRequiredService<IOptions<R2BlobStorageOptions>>();
            var mimeTypeProvider = serviceProvider.GetRequiredService<IMimeTypeProvider>();
            var clock = serviceProvider.GetRequiredService<IClock>();
            var awsOptions = new AwsBlobStorageOptions();
            _ApplyR2ForcedDefaults(awsOptions);
            var logger = serviceProvider.GetService<ILogger<AwsBlobStorage>>() ?? NullLogger<AwsBlobStorage>.Instance;
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

        // Deliberately NO IBlobContainerManager registration: R2's object-scoped tokens cannot create or manage
        // buckets, and ensuring a missing bucket would not make a later upload succeed. Because container
        // management is a separately-resolved DI capability (not a cast from IBlobStorage), omitting the
        // registration makes GetService/GetKeyedService<IBlobContainerManager> honestly return null for R2
        // (KTD5 / U12). R2 bucket provisioning is out of band (IaC/dashboard). Do not copy the AWS registration.
        return services;
    }

    internal static IServiceCollection AddBlobsNamedCore(IServiceCollection services, string name)
    {
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

        // Presigned URLs are a cast from the same instance (R2 supports SigV4 presigning), but container
        // management is not: no keyed IBlobContainerManager is registered for this named R2 instance, so
        // GetKeyedService<IBlobContainerManager>(name) honestly returns null (KTD5 / U12). Do not add one.
        return services;
    }

    private static void _ApplyR2ForcedDefaults(AwsBlobStorageOptions options)
    {
        // R2 has no ACLs and rejects chunked/payload signing.
        options.CannedAcl = null;
        options.UseChunkEncoding = false;
        options.DisablePayloadSigning = true;
    }
}

/// <summary>Extension methods to register the Cloudflare R2 blob storage provider as a named store.</summary>
[PublicAPI]
public static class SetupCloudflareR2BlobNamed
{
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
                SetupCloudflareR2Blob.AddBlobsNamedCore(services, name);
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
                SetupCloudflareR2Blob.AddBlobsNamedCore(services, name);
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
                SetupCloudflareR2Blob.AddBlobsNamedCore(services, name);
            });

            return instance;
        }
    }
}
