// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Runtime;
using Amazon.S3;
using Headless.Blobs.Aws;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Blobs.CloudflareR2;

[PublicAPI]
public static class SetupCloudflareR2Blob
{
    // Cloudflare R2 signs every request against the "auto" region.
    private const string _Region = "auto";

    extension(IServiceCollection services)
    {
        /// <summary>Registers Cloudflare R2 blob storage, reusing the S3 engine behind an R2-tuned client.</summary>
        public IServiceCollection AddCloudflareR2BlobStorage(Action<R2BlobStorageOptions, IServiceProvider> setupAction)
        {
            services.Configure<R2BlobStorageOptions, R2BlobStorageOptionsValidator>(setupAction);

            return services._AddCloudflareR2Core();
        }

        /// <summary>Registers Cloudflare R2 blob storage, reusing the S3 engine behind an R2-tuned client.</summary>
        public IServiceCollection AddCloudflareR2BlobStorage(Action<R2BlobStorageOptions> setupAction)
        {
            services.Configure<R2BlobStorageOptions, R2BlobStorageOptionsValidator>(setupAction);

            return services._AddCloudflareR2Core();
        }

        /// <summary>Registers Cloudflare R2 blob storage, reusing the S3 engine behind an R2-tuned client.</summary>
        public IServiceCollection AddCloudflareR2BlobStorage(IConfiguration config)
        {
            services.Configure<R2BlobStorageOptions, R2BlobStorageOptionsValidator>(config);

            return services._AddCloudflareR2Core();
        }

        private IServiceCollection _AddCloudflareR2Core()
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

                var config = new AmazonS3Config
                {
                    ServiceURL = options.GetEffectiveEndpointUrl(),
                    ForcePathStyle = true,
                    AuthenticationRegion = _Region,
                    // SDK v4 defaults add CRC checksums that R2 rejects; only send them when an operation requires it.
                    RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                    ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
                };

                var credentials = new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey);

                return new AmazonS3Client(credentials, config);
            });

            services.TryAddSingleton<IBlobNamingNormalizer, R2BlobNamingNormalizer>();
            services.AddSingleton<IBlobStorage, AwsBlobStorage>();

            return services;
        }
    }
}
