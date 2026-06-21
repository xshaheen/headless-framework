// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3;
using Headless.Blobs.Aws;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Blobs.CloudflareR2;

/// <summary>Extension methods to register the Cloudflare R2 blob storage provider.</summary>
[PublicAPI]
public static class SetupCloudflareR2Blob
{
    extension(IServiceCollection services)
    {
        /// <summary>Registers Cloudflare R2 blob storage, reusing the S3 engine behind an R2-tuned client.</summary>
        public IServiceCollection AddCloudflareR2BlobStorage(Action<R2BlobStorageOptions, IServiceProvider> setupAction)
        {
            services.Configure<R2BlobStorageOptions, R2BlobStorageOptionsValidator>(setupAction);

            return services._AddCore();
        }

        /// <summary>Registers Cloudflare R2 blob storage, reusing the S3 engine behind an R2-tuned client.</summary>
        public IServiceCollection AddCloudflareR2BlobStorage(Action<R2BlobStorageOptions> setupAction)
        {
            services.Configure<R2BlobStorageOptions, R2BlobStorageOptionsValidator>(setupAction);

            return services._AddCore();
        }

        /// <summary>Registers Cloudflare R2 blob storage, reusing the S3 engine behind an R2-tuned client.</summary>
        public IServiceCollection AddCloudflareR2BlobStorage(IConfiguration config)
        {
            services.Configure<R2BlobStorageOptions, R2BlobStorageOptionsValidator>(config);

            return services._AddCore();
        }

        private IServiceCollection _AddCore()
        {
            // R2-safe behavior defaults on the reused AWS engine: R2 has no ACLs, rejects chunked/payload signing,
            // and object-scoped tokens cannot create buckets.
            services.Configure<AwsBlobStorageOptions>(options =>
            {
                options.CannedAcl = null;
                options.UseChunkEncoding = false;
                options.DisablePayloadSigning = true;
                options.AutoCreateContainer = false;
            });

            services.TryAddSingleton<IAmazonS3>(serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<R2BlobStorageOptions>>().Value;

                return R2ClientFactory.Create(options);
            });

            services.TryAddSingleton<IBlobNamingNormalizer, R2BlobNamingNormalizer>();
            services.AddSingleton<IBlobStorage, AwsBlobStorage>();

            // The engine implements IPresignedUrlBlobStorage; expose it for direct injection too (same instance).
            services.TryAddSingleton<IPresignedUrlBlobStorage>(serviceProvider =>
                (IPresignedUrlBlobStorage)serviceProvider.GetRequiredService<IBlobStorage>()
            );

            return services;
        }
    }
}
