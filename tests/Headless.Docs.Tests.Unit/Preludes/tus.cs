// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the store, client, and options variables the tus guide's examples carry over from earlier snippets.

global using static TusAmbient;
using Azure.Storage.Blobs;
using Headless.Tus;

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class TusAmbient
{
    public static TusAzureStore tusStore => null!;

    public static BlobServiceClient blobServiceClient => null!;

    public static TusAzureStoreOptions options => null!;
}
