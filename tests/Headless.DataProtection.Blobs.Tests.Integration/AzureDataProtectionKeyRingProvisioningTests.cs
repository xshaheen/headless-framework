// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Headless.Blobs;

namespace Tests;

[Collection<AzuriteFixture>]
public sealed class AzureDataProtectionKeyRingProvisioningTests(AzuriteFixture fixture)
    : DataProtectionKeyRingProvisioningTestsBase
{
    protected override void UseBlobs(HeadlessBlobsSetupBuilder blobs)
    {
        // Pinned to a service version the Azurite image accepts, as the storage tests do.
        blobs.UseAzure(
            _ => { },
            clientFactory: _ => new BlobServiceClient(
                fixture.Container.GetConnectionString(),
                new BlobClientOptions(BlobClientOptions.ServiceVersion.V2024_11_04)
            )
        );
    }
}
