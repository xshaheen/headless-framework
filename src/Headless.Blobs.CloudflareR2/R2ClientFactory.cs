// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Amazon.Runtime;
using Amazon.S3;

[assembly: InternalsVisibleTo("Headless.Blobs.CloudflareR2.Tests.Integration")]

namespace Headless.Blobs.CloudflareR2;

/// <summary>
/// Builds the R2-tuned <see cref="IAmazonS3"/>. Centralized so the DI setup and the conformance test fixture
/// share one source of truth for the R2 client configuration and cannot drift apart.
/// </summary>
internal static class R2ClientFactory
{
    /// <summary>Cloudflare R2 signs every request against the <c>auto</c> region.</summary>
    public const string Region = "auto";

    public static IAmazonS3 Create(R2BlobStorageOptions options)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = options.GetEffectiveEndpointUrl(),
            ForcePathStyle = true,
            AuthenticationRegion = Region,
            // SDK v4 defaults add CRC checksums that R2 rejects; only send them when an operation requires it.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };

        return new AmazonS3Client(new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey), config);
    }
}
