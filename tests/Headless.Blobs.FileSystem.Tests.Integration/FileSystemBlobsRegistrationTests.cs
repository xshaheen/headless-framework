// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.Blobs.FileSystem;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Verifies <c>AddHeadlessBlobs</c> default + named registration and per-instance isolation for the file system
/// provider. Disk-based — no container required.
/// </summary>
public sealed class FileSystemBlobsRegistrationTests : TestBase
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
        string[] container = ["bucket"];

        // when
        await images.UploadContentAsync(container, "a.txt", "hello", AbortToken);

        // then — write to one named store is not visible in the other, and no default store exists
        (await images.GetBlobContentAsync(container, "a.txt", AbortToken))
            .Should()
            .Be("hello");
        (await docs.GetBlobContentAsync(container, "a.txt", AbortToken)).Should().BeNull();
        serviceProvider.GetService<IBlobStorage>().Should().BeNull();
    }
}
