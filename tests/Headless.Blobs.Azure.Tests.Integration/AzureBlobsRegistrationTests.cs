// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Headless.Abstractions;
using Headless.Blobs;
using Headless.Blobs.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tests;

/// <summary>
/// Verifies <c>AddHeadlessBlobs</c> default + named registration and per-instance isolation for the Azure Blob
/// Storage provider. Registration-shape only — no network I/O, no Azurite container required.
///
/// <para>
/// An anonymous endpoint <see cref="System.Uri"/> constructs each <see cref="BlobServiceClient"/>; the client
/// object is created but no Azure calls are made. This lets client construction succeed without a live endpoint
/// or credentials while still exercising the full DI wiring.
/// </para>
/// </summary>
public sealed class AzureBlobsRegistrationTests
{
    // Anonymous endpoint URI — client construction succeeds without a connection string or credentials;
    // registration-shape tests make no network calls.
    private const string _BlobServiceUri = "https://devstoreaccount1.blob.core.windows.net/";

    [Fact]
    public async Task default_store_is_injectable_and_named_stores_resolve_via_provider()
    {
        // given — default store uses ambient BlobServiceClient from DI;
        //         named stores supply per-store clients via clientFactory (simulating different accounts)
        var defaultClient = new BlobServiceClient(new Uri(_BlobServiceUri));
        var archiveClient = new BlobServiceClient(new Uri(_BlobServiceUri));
        var docsClient = new BlobServiceClient(new Uri(_BlobServiceUri));

        var services = new ServiceCollection();
        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMimeTypeProvider, MimeTypeProvider>();
        services.TryAddSingleton<IClock, Clock>();
        services.AddSingleton(defaultClient); // ambient client for the default store

        services.AddHeadlessBlobs(blobs =>
        {
            // Default store: resolves BlobServiceClient from DI
            blobs.UseAzure(options => options.AutoCreateContainer = true);

            // Named "archive": per-store client factory — simulates a second Azure account
            blobs.AddNamed(
                "archive",
                instance =>
                    instance.UseAzure(
                        setupAction: options => options.AutoCreateContainer = false,
                        clientFactory: _ => archiveClient
                    )
            );

            // Named "docs": per-store client factory
            blobs.AddNamed(
                "docs",
                instance => instance.UseAzure(setupAction: options => { }, clientFactory: _ => docsClient)
            );
        });

        await using var sp = services.BuildServiceProvider();

        // when
        var defaultStorage = sp.GetService<IBlobStorage>();
        var provider = sp.GetRequiredService<IBlobStorageProvider>();
        var archive = provider.GetStorage("archive");
        var docs = provider.GetStorage("docs");

        // then — default store is resolvable as plain IBlobStorage
        defaultStorage.Should().NotBeNull();
        defaultStorage.Should().BeOfType<AzureBlobStorage>();

        // named stores resolve correctly through the provider
        archive.Should().NotBeNull();
        docs.Should().NotBeNull();

        // each named store is a distinct instance
        archive.Should().NotBeSameAs(docs);
        archive.Should().NotBeSameAs(defaultStorage);

        // keyed resolution is consistent with provider resolution
        sp.GetRequiredKeyedService<IBlobStorage>("archive").Should().BeSameAs(archive);
        sp.GetRequiredKeyedService<IBlobStorage>("docs").Should().BeSameAs(docs);
    }

    [Fact]
    public async Task default_and_named_stores_cast_to_presigned_url_storage()
    {
        // given
        var client = new BlobServiceClient(new Uri(_BlobServiceUri));

        var services = new ServiceCollection();
        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMimeTypeProvider, MimeTypeProvider>();
        services.TryAddSingleton<IClock, Clock>();
        services.AddSingleton(client);

        services.AddHeadlessBlobs(blobs =>
        {
            blobs.UseAzure(options => { });

            blobs.AddNamed(
                "cdn",
                instance => instance.UseAzure(setupAction: options => { }, clientFactory: _ => client)
            );
        });

        await using var sp = services.BuildServiceProvider();

        // when
        var defaultStorage = sp.GetRequiredService<IBlobStorage>();
        var defaultPresigned = sp.GetRequiredService<IPresignedUrlBlobStorage>();
        var namedPresigned = sp.GetRequiredKeyedService<IPresignedUrlBlobStorage>("cdn");

        // then — AzureBlobStorage implements IPresignedUrlBlobStorage; the forwarding registrations resolve
        defaultStorage.Should().BeAssignableTo<IPresignedUrlBlobStorage>();
        defaultPresigned.Should().BeSameAs(defaultStorage);

        namedPresigned.Should().NotBeNull();
        namedPresigned.Should().BeSameAs(sp.GetRequiredKeyedService<IBlobStorage>("cdn"));
    }

    [Fact]
    public async Task named_only_setup_leaves_no_default_blob_storage()
    {
        // given — no default provider configured; named stores only
        var client = new BlobServiceClient(new Uri(_BlobServiceUri));

        var services = new ServiceCollection();
        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMimeTypeProvider, MimeTypeProvider>();
        services.TryAddSingleton<IClock, Clock>();

        services.AddHeadlessBlobs(blobs =>
        {
            blobs.AddNamed(
                "reports",
                instance => instance.UseAzure(setupAction: options => { }, clientFactory: _ => client)
            );
        });

        await using var sp = services.BuildServiceProvider();

        // then — unkeyed IBlobStorage is not registered
        sp.GetService<IBlobStorage>().Should().BeNull();
        sp.GetRequiredKeyedService<IBlobStorage>("reports").Should().NotBeNull();
    }
}
