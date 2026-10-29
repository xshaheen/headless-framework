// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Headless.Testing.Testcontainers;
using Headless.Tus;
using Headless.Tus.Internal;
using Microsoft.Extensions.Logging;

namespace Tests.TestSetup;

[UsedImplicitly]
[CollectionDefinition]
public sealed class TusAzureFixture
    : HeadlessAzuriteFixture,
        ICollectionFixture<TusAzureFixture>,
        ITusConformanceBackend<TusAzureStore>
{
    /// <summary>The container every conformance scenario and the Azure-only store tests share.</summary>
    public const string ContainerName = "tuscontainer";

    /// <summary>The blob prefix under <see cref="ContainerName" /> the shared store writes to.</summary>
    public const string BlobPrefix = "tusfiles/";

    public BlobServiceClient CreateBlobServiceClient()
    {
        return new BlobServiceClient(
            Container.GetConnectionString(),
            new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
        );
    }

    // The store does not create its container; the deploy-time provisioner does, so every store is
    // provisioned before a scenario touches it.
    public async Task<TusAzureStore> CreateStoreAsync(ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var blobServiceClient = CreateBlobServiceClient();
        var storeOptions = new TusAzureStoreOptions { ContainerName = ContainerName, BlobPrefix = BlobPrefix };

        await new TusAzureContainerProvisioner(blobServiceClient, storeOptions).ProvisionAsync(cancellationToken);

        return new TusAzureStore(blobServiceClient, storeOptions, loggerFactory: loggerFactory);
    }
}
