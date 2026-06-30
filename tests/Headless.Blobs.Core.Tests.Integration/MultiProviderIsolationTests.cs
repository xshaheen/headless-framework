// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Cross-provider conformance: registers a FileSystem default plus named AWS, Azure, R2, and FileSystem stores
// in a single AddHeadlessBlobs call and asserts name resolution, per-instance isolation, and per-store presigned
// capability. FileSystem assertions perform real disk I/O; cloud stores are registration-shape only (lazy client
// construction, no network) so no Docker is required.

using Amazon;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using Azure.Storage.Blobs;
using Headless.Abstractions;
using Headless.Blobs;
using Headless.Blobs.Aws;
using Headless.Blobs.Azure;
using Headless.Blobs.CloudflareR2;
using Headless.Blobs.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests;

public sealed class MultiProviderIsolationTests
{
    private static AWSOptions _DummyAwsOptions() =>
        new() { Region = RegionEndpoint.USEast1, Credentials = new BasicAWSCredentials("k", "s") };

    private static BlobServiceClient _DummyAzureClient() =>
        new(new Uri("https://devstoreaccount1.blob.core.windows.net/"));

    private static void _ConfigureR2(R2BlobStorageOptions options)
    {
        options.AccountId = "acct";
        options.AccessKeyId = "ak";
        options.SecretAccessKey = "sk";
    }

    private static ServiceProvider _BuildMixedProvider(string defaultDir, string scratchDir)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMimeTypeProvider, MimeTypeProvider>();
        services.TryAddSingleton<IClock, Clock>();

        services.AddHeadlessBlobs(blobs =>
        {
            blobs.UseFileSystem(options => options.BaseDirectoryPath = defaultDir); // default, non-presigned
            blobs.AddNamed("s3", instance => instance.UseAws(options => { }, _DummyAwsOptions()));
            blobs.AddNamed(
                "azure",
                instance => instance.UseAzure(setupAction: options => { }, clientFactory: _ => _DummyAzureClient())
            );
            blobs.AddNamed("r2", instance => instance.UseCloudflareR2(_ConfigureR2));
            blobs.AddNamed(
                "scratch",
                instance => instance.UseFileSystem(options => options.BaseDirectoryPath = scratchDir)
            );
        });

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task mixed_providers_resolve_distinctly_and_default_is_file_system()
    {
        // given
        var defaultDir = Directory.CreateTempSubdirectory().FullName;
        var scratchDir = Directory.CreateTempSubdirectory().FullName;
        await using var sp = _BuildMixedProvider(defaultDir, scratchDir);
        var provider = sp.GetRequiredService<IBlobStorageProvider>();

        // when
        var defaultStorage = sp.GetRequiredService<IBlobStorage>();
        var s3 = provider.GetStorage("s3");
        var azure = provider.GetStorage("azure");
        var r2 = provider.GetStorage("r2");
        var scratch = provider.GetStorage("scratch");

        // then — default is the FileSystem store; every named store is a distinct instance
        defaultStorage.Should().BeOfType<FileSystemBlobStorage>();
        new object[] { defaultStorage, s3, azure, r2, scratch }
            .Should()
            .OnlyHaveUniqueItems();
        sp.GetRequiredKeyedService<IBlobStorage>("s3").Should().BeSameAs(s3);
        sp.GetRequiredKeyedService<IBlobStorage>("scratch").Should().BeSameAs(scratch);
    }

    [Fact]
    public async Task presigned_capability_is_per_store()
    {
        // given
        var defaultDir = Directory.CreateTempSubdirectory().FullName;
        var scratchDir = Directory.CreateTempSubdirectory().FullName;
        await using var sp = _BuildMixedProvider(defaultDir, scratchDir);

        // then — presign-capable providers expose keyed IPresignedUrlBlobStorage; FileSystem stores do not
        sp.GetKeyedService<IPresignedUrlBlobStorage>("s3").Should().NotBeNull();
        sp.GetKeyedService<IPresignedUrlBlobStorage>("azure").Should().NotBeNull();
        sp.GetKeyedService<IPresignedUrlBlobStorage>("r2").Should().NotBeNull();
        sp.GetKeyedService<IPresignedUrlBlobStorage>("scratch").Should().BeNull();
        sp.GetService<IPresignedUrlBlobStorage>().Should().BeNull(); // default FileSystem store
    }

    [Fact]
    public async Task file_system_stores_are_io_isolated()
    {
        // given
        var defaultDir = Directory.CreateTempSubdirectory().FullName;
        var scratchDir = Directory.CreateTempSubdirectory().FullName;
        await using var sp = _BuildMixedProvider(defaultDir, scratchDir);
        var provider = sp.GetRequiredService<IBlobStorageProvider>();
        var defaultStorage = sp.GetRequiredService<IBlobStorage>();
        var scratch = provider.GetStorage("scratch");
        var location = new BlobLocation("bucket", "a.txt");

        // when
        await defaultStorage.UploadContentAsync(location, "hello");

        // then
        (await defaultStorage.GetBlobContentAsync(location))
            .Should()
            .Be("hello");
        (await scratch.GetBlobContentAsync(location)).Should().BeNull();
    }
}
