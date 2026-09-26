// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the variables the blobs guide's examples assume from the surrounding application.

// Provider SDK namespaces the examples use; a consumer's IDE adds these usings.
global using Amazon;
global using Amazon.Extensions.NETCore.Setup;
global using Amazon.Runtime;
global using Azure.Storage.Blobs;
global using static BlobsAmbient;
global using StackExchange.Redis;
using Headless.Blobs;

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class BlobsAmbient
{
    public static IBlobStorage storage => null!;

    public static IPresignedUrlBlobStorage presigned => null!;

    public static BlobLocation location => default!;

    public static TimeSpan expiry => default;

    public static Stream stream => null!;

    public static IConnectionMultiplexer redis => null!;
}
