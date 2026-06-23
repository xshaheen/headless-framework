// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Extensions.NETCore.Setup;
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
}
