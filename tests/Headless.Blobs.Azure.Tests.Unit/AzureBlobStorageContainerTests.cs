// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Headless.Blobs.Azure;
using Headless.Testing.Tests;
using NSubstitute;

namespace Tests;

public sealed class AzureBlobContainerManagerTests : TestBase
{
    [Fact]
    public async Task ensure_container_creates_at_most_once()
    {
        var containerClient = Substitute.For<BlobContainerClient>();
        var serviceClient = Substitute.For<BlobServiceClient>();
        serviceClient.GetBlobContainerClient(Arg.Any<string>()).Returns(containerClient);

        // Container lifecycle now lives on the separately-resolved manager, not on AzureBlobStorage.
        var sut = new AzureBlobContainerManager(serviceClient, new AzureBlobNamingNormalizer(), PublicAccessType.None);

        await sut.EnsureContainerAsync("mycontainer", AbortToken);
        await sut.EnsureContainerAsync("mycontainer", AbortToken);

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
