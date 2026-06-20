// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Blobs.Aws;
using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#pragma warning disable CA1708 // multiple extension blocks emit marker members differing only by case
namespace Headless.Blobs.CloudflareR2;

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
                services._AddR2DefaultCore();
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
                services._AddR2DefaultCore();
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
                services._AddR2DefaultCore();
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
                services._AddR2NamedCore(name);
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
                services._AddR2NamedCore(name);
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
                services._AddR2NamedCore(name);
            });

            return instance;
        }
    }

    extension(IServiceCollection services)
    {
        private IServiceCollection _AddR2DefaultCore()
        {
            services.AddBlobStorageProvider();

            // R2-safe behavior on the reused AWS engine, applied to the default (unnamed) AwsBlobStorageOptions.
            services.Configure<AwsBlobStorageOptions>(_ApplyR2ForcedDefaults);

            services.AddSingleton<IBlobStorage>(serviceProvider => new AwsBlobStorage(
                R2ClientFactory.Create(serviceProvider.GetRequiredService<IOptions<R2BlobStorageOptions>>().Value),
                serviceProvider.GetRequiredService<IMimeTypeProvider>(),
                serviceProvider.GetRequiredService<IClock>(),
                serviceProvider.GetRequiredService<IOptions<AwsBlobStorageOptions>>(),
                new R2BlobNamingNormalizer(),
                serviceProvider.GetService<ILogger<AwsBlobStorage>>()
            ));

            services.AddSingleton<IPresignedUrlBlobStorage>(serviceProvider =>
                (IPresignedUrlBlobStorage)serviceProvider.GetRequiredService<IBlobStorage>()
            );

            return services;
        }

        private IServiceCollection _AddR2NamedCore(string name)
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

    private static void _ApplyR2ForcedDefaults(AwsBlobStorageOptions options)
    {
        // R2 has no ACLs, rejects chunked/payload signing, and object-scoped tokens cannot create buckets.
        options.CannedAcl = null;
        options.UseChunkEncoding = false;
        options.DisablePayloadSigning = true;
        options.AutoCreateContainer = false;
    }
}
