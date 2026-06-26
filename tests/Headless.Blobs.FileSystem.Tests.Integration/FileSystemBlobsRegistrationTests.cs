// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.Blobs.FileSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Verifies <c>AddHeadlessBlobs</c> default + named registration and per-instance isolation for the file system
/// provider. Disk-based — no container required.
/// </summary>
public sealed class FileSystemBlobsRegistrationTests
{
    [Fact]
    public async Task default_store_is_injectable_and_named_stores_resolve_via_provider()
    {
        // given
        var defaultDir = Directory.CreateTempSubdirectory().FullName;
        var imagesDir = Directory.CreateTempSubdirectory().FullName;
        var docsDir = Directory.CreateTempSubdirectory().FullName;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessBlobs(blobs =>
        {
            blobs.UseFileSystem(options => options.BaseDirectoryPath = defaultDir);
            blobs.AddNamed(
                "images",
                instance => instance.UseFileSystem(options => options.BaseDirectoryPath = imagesDir)
            );
            blobs.AddNamed("docs", instance => instance.UseFileSystem(options => options.BaseDirectoryPath = docsDir));
        });
        await using var serviceProvider = services.BuildServiceProvider();

        // when
        var defaultStorage = serviceProvider.GetService<IBlobStorage>();
        var provider = serviceProvider.GetRequiredService<IBlobStorageProvider>();
        var images = provider.GetStorage("images");
        var docs = provider.GetStorage("docs");

        // then
        defaultStorage.Should().NotBeNull();
        images.Should().NotBeSameAs(docs);
        serviceProvider.GetRequiredKeyedService<IBlobStorage>("images").Should().BeSameAs(images);

        // the file-system container-management capability is a separately-registered service (resolved, not cast):
        // the default store registers an unkeyed manager and each named store registers a keyed one.
        serviceProvider.GetService<IBlobContainerManager>().Should().NotBeNull();
        serviceProvider.GetRequiredKeyedService<IBlobContainerManager>("images").Should().NotBeNull();
        serviceProvider.GetRequiredKeyedService<IBlobContainerManager>("docs").Should().NotBeNull();
    }

    [Fact]
    public async Task named_stores_are_isolated_by_base_directory()
    {
        // given
        var imagesDir = Directory.CreateTempSubdirectory().FullName;
        var docsDir = Directory.CreateTempSubdirectory().FullName;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessBlobs(blobs =>
        {
            blobs.AddNamed(
                "images",
                instance => instance.UseFileSystem(options => options.BaseDirectoryPath = imagesDir)
            );
            blobs.AddNamed("docs", instance => instance.UseFileSystem(options => options.BaseDirectoryPath = docsDir));
        });
        await using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<IBlobStorageProvider>();
        var images = provider.GetStorage("images");
        var docs = provider.GetStorage("docs");
        var location = new BlobLocation("bucket", "a.txt");

        // when
        await images.UploadContentAsync(location, "hello");

        // then — write to one named store is not visible in the other, and no default store exists
        (await images.GetBlobContentAsync(location))
            .Should()
            .Be("hello");
        (await docs.GetBlobContentAsync(location)).Should().BeNull();
        serviceProvider.GetService<IBlobStorage>().Should().BeNull();
    }
}
