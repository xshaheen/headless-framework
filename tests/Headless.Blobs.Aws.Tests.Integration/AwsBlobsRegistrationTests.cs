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

        // then — the AWS engine implements IPresignedUrlBlobStorage, but no global alias is registered
        defaultStorage.Should().BeAssignableTo<IPresignedUrlBlobStorage>();
        serviceProvider.GetService<IPresignedUrlBlobStorage>().Should().BeNull();
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

        // and — a named-only (no default) setup leaks no unkeyed presigned alias
        serviceProvider.GetService<IPresignedUrlBlobStorage>().Should().BeNull();
    }

    [Fact]
    public async Task default_store_without_aws_options_uses_credential_chain()
    {
        // given — no AWSOptions supplied, so S3ClientFactory builds `new AmazonS3Client()`, which resolves
        // region/credentials from the SDK chain. Supply a region via the environment so client construction
        // succeeds without a configured profile; restore it afterward.
        var previousRegion = Environment.GetEnvironmentVariable("AWS_REGION");
        Environment.SetEnvironmentVariable("AWS_REGION", "us-east-1");

        try
        {
            var services = BuildBaseServices();
            services.AddHeadlessBlobs(blobs => blobs.UseAws(options => { }));
            await using var serviceProvider = services.BuildServiceProvider();

            // when — resolving the default store exercises the new AmazonS3Client() (credential-chain) branch
            var storage = serviceProvider.GetRequiredService<IBlobStorage>();

            // then
            storage.Should().BeAssignableTo<IPresignedUrlBlobStorage>();
        }
        finally
        {
            Environment.SetEnvironmentVariable("AWS_REGION", previousRegion);
        }
    }
}
