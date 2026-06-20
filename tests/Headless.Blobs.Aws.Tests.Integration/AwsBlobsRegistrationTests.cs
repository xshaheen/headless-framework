// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Registration-shape-only tests: build the DI container and resolve instances.
// No S3 I/O is performed so no LocalStack / Docker is required.
// Each store is configured with dummy credentials + a real region so AmazonS3Client
// construction succeeds; all network I/O is deferred to actual S3 operations that we never invoke.

using Amazon;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using Headless.Abstractions;
using Headless.Blobs;
using Headless.Blobs.Aws;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests;

public sealed class AwsBlobsRegistrationTests
{
    // Dummy AWSOptions so AmazonS3Client construction succeeds without real credentials.
    // No network I/O occurs during construction; errors only surface on actual S3 calls.
    private static AWSOptions DummyAwsOptions() =>
        new()
        {
            Region = RegionEndpoint.USEast1,
            Credentials = new BasicAWSCredentials("test-access-key", "test-secret-key"),
        };

    private static ServiceCollection BuildBaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMimeTypeProvider, MimeTypeProvider>();
        services.TryAddSingleton<IClock, Clock>();

        return services;
    }

    [Fact]
    public async Task default_store_is_injectable_and_named_stores_resolve_via_provider()
    {
        // given
        var services = BuildBaseServices();

        services.AddHeadlessBlobs(blobs =>
        {
            blobs.UseAws(options => { }, DummyAwsOptions());
            blobs.AddNamed("media", instance => instance.UseAws(options => { }, DummyAwsOptions()));
            blobs.AddNamed("docs", instance => instance.UseAws(options => { }, DummyAwsOptions()));
        });

        await using var serviceProvider = services.BuildServiceProvider();

        // when
        var defaultStorage = serviceProvider.GetService<IBlobStorage>();
        var blobProvider = serviceProvider.GetRequiredService<IBlobStorageProvider>();
        var media = blobProvider.GetStorage("media");
        var docs = blobProvider.GetStorage("docs");

        // then
        defaultStorage.Should().NotBeNull();
        media.Should().NotBeSameAs(docs);
        serviceProvider.GetRequiredKeyedService<IBlobStorage>("media").Should().BeSameAs(media);
        serviceProvider.GetRequiredKeyedService<IBlobStorage>("docs").Should().BeSameAs(docs);
    }

    [Fact]
    public async Task named_stores_are_distinct_instances()
    {
        // given
        var services = BuildBaseServices();

        services.AddHeadlessBlobs(blobs =>
        {
            blobs.AddNamed("store-a", instance => instance.UseAws(options => { }, DummyAwsOptions()));
            blobs.AddNamed("store-b", instance => instance.UseAws(options => { }, DummyAwsOptions()));
        });

        await using var serviceProvider = services.BuildServiceProvider();
        var blobProvider = serviceProvider.GetRequiredService<IBlobStorageProvider>();

        // when
        var storeA = blobProvider.GetStorage("store-a");
        var storeB = blobProvider.GetStorage("store-b");

        // then — distinct keyed singletons; no unkeyed default
        storeA.Should().NotBeSameAs(storeB);
        serviceProvider.GetService<IBlobStorage>().Should().BeNull();
    }

    [Fact]
    public async Task default_store_casts_to_IPresignedUrlBlobStorage()
    {
        // given
        var services = BuildBaseServices();

        services.AddHeadlessBlobs(blobs =>
        {
            blobs.UseAws(options => { }, DummyAwsOptions());
        });

        await using var serviceProvider = services.BuildServiceProvider();

        // when
        var defaultStorage = serviceProvider.GetRequiredService<IBlobStorage>();
        var presigned = serviceProvider.GetRequiredService<IPresignedUrlBlobStorage>();

        // then — the AWS engine implements IPresignedUrlBlobStorage; the unkeyed alias points to the same instance
        defaultStorage.Should().BeAssignableTo<IPresignedUrlBlobStorage>();
        presigned.Should().BeSameAs(defaultStorage);
    }

    [Fact]
    public async Task named_store_resolves_as_keyed_IPresignedUrlBlobStorage()
    {
        // given
        var services = BuildBaseServices();

        services.AddHeadlessBlobs(blobs =>
        {
            blobs.AddNamed("assets", instance => instance.UseAws(options => { }, DummyAwsOptions()));
        });

        await using var serviceProvider = services.BuildServiceProvider();

        // when
        var storage = serviceProvider.GetRequiredKeyedService<IBlobStorage>("assets");
        var presigned = serviceProvider.GetRequiredKeyedService<IPresignedUrlBlobStorage>("assets");

        // then — keyed presigned alias points to the same keyed storage instance
        storage.Should().BeAssignableTo<IPresignedUrlBlobStorage>();
        presigned.Should().BeSameAs(storage);
    }
}
