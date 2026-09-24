// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using Amazon.S3;

namespace Headless.Blobs.Aws;

/// <summary>
/// Builds a per-store <see cref="IAmazonS3"/> client for each named or default AWS blob storage instance.
/// Centralized so the DI setup and the conformance test fixture share one source of truth for the S3 client
/// configuration and cannot drift apart.
/// <para>
/// <see cref="AwsBlobStorageOptions"/> carries S3 behavior settings (ACL, chunk encoding, payload signing,
/// auto-create) but does <em>not</em> carry connection settings (endpoint, region, credentials). Connection
/// settings are supplied via the optional <c>awsOptions</c> parameter on <see cref="Create"/>, which is
/// forwarded to the AWS SDK's <see cref="AWSOptions.CreateServiceClient{T}"/> factory. When no options are
/// supplied, the SDK resolves credentials and region through its standard chain (environment variables, shared
/// credentials file, instance metadata, etc.).
/// </para>
/// </summary>
internal static class S3ClientFactory
{
    /// <summary>
    /// Creates an <see cref="IAmazonS3"/> client for a single blob storage store.
    /// </summary>
    /// <param name="awsOptions">
    /// Optional per-store AWS SDK options (region, credentials, endpoint). When <see langword="null"/> the SDK
    /// credential and region chain applies.
    /// </param>
    /// <returns>A configured <see cref="IAmazonS3"/> instance owned by the caller.</returns>
    public static IAmazonS3 Create(AWSOptions? awsOptions = null)
    {
        if (awsOptions is not null)
        {
            return awsOptions.CreateServiceClient<IAmazonS3>();
        }

        return new AmazonS3Client();
    }

    /// <summary>Creates an <see cref="IAmazonS3"/> client tuned for an S3-compatible endpoint such as MinIO.</summary>
    /// <param name="options">The validated S3-compatible connection options.</param>
    /// <returns>A configured <see cref="IAmazonS3"/> instance owned by the caller.</returns>
    public static IAmazonS3 CreateS3Compatible(S3CompatibleBlobStorageOptions options)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = options.ServiceUrl,
            // Self-hosted endpoints rarely have wildcard DNS for virtual-hosted bucket names.
            ForcePathStyle = true,
            AuthenticationRegion = options.AuthenticationRegion,
            // SDK v4 defaults add CRC checksum headers and trailers that many S3-compatible servers reject; only
            // send them when an operation requires it.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };

        return new AmazonS3Client(new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey), config);
    }

    /// <summary>Storage behavior forced for every S3-compatible store.</summary>
    public static void ApplyS3CompatibleDefaults(AwsBlobStorageOptions target, S3CompatibleBlobStorageOptions source)
    {
        // Object ACLs are an AWS-specific feature; MinIO and most compatible servers ignore or reject the header.
        target.CannedAcl = null;
        // Streaming aws-chunked uploads are the SDK mode compatible servers most often reject; a buffered body
        // with a plain Content-Length is universally accepted.
        target.UseChunkEncoding = false;
        // Keep the payload signed: the SDK refuses unsigned payloads over http, and a signed payload is the SigV4
        // baseline every compatible server implements.
        target.DisablePayloadSigning = false;
        target.MaxBulkParallelism = source.MaxBulkParallelism;
    }
}
