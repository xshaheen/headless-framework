// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Headless.Abstractions;
using Headless.Blobs.Azure;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class AzureBlobStorageContainerTests : TestBase
{
    [Fact]
    public async Task create_container_ensures_at_most_once()
    {
        var containerClient = Substitute.For<BlobContainerClient>();
        var serviceClient = Substitute.For<BlobServiceClient>();
        serviceClient.GetBlobContainerClient(Arg.Any<string>()).Returns(containerClient);

        await using var sut = new AzureBlobStorage(
            serviceClient,
            new MimeTypeProvider(),
            new Clock(TimeProvider.System),
            new OptionsWrapper<AzureStorageOptions>(new AzureStorageOptions()),
            new AzureBlobNamingNormalizer(),
            NullLogger<AzureBlobStorage>.Instance
        );

        await sut.CreateContainerAsync(["mycontainer"], AbortToken);
        await sut.CreateContainerAsync(["mycontainer"], AbortToken);

        // The second call is served from the per-instance cache; CreateIfNotExists runs once.
        await containerClient
            .Received(1)
            .CreateIfNotExistsAsync(
                Arg.Any<PublicAccessType>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<BlobContainerEncryptionScopeOptions>(),
                Arg.Any<CancellationToken>()
            );
    }
}
