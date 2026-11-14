// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Headless.Hosting;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tests.TestSetup;
using tusdotnet.Models.Concatenation;
using TusAzureMetadata = Headless.Tus.Models.TusAzureMetadata;

namespace Tests.Tenancy;

/// <summary>
/// With uploads scoped by tenant, the store an application registers keeps each upload's blob under its tenant's
/// folder, serves an upload only inside its tenant, and still removes expired uploads and purges the store from
/// outside any tenant. Each test owns a blob prefix, because a sweep and a purge cover their whole prefix.
/// </summary>
[Collection<TusAzureFixture>]
public sealed class TusAzureTenancyTests(TusAzureFixture fixture) : TestBase
{
    private const string _Acme = "acme";
    private const string _Globex = "globex";

    private readonly BlobServiceClient _blobServiceClient = fixture.CreateBlobServiceClient();

    private readonly TusAzureStoreOptions _options = new()
    {
        ContainerName = TusAzureFixture.ContainerName,
        BlobPrefix = $"tenancy-{Guid.NewGuid():N}/",
    };

    private ServiceProvider _provider = null!;
    private TusAzureStore _store = null!;
    private ICurrentTenant _tenant = null!;

    private BlobContainerClient _Container => _blobServiceClient.GetBlobContainerClient(_options.ContainerName);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        // The store does not create its container; the deploy-time provisioner does.
        await new TusAzureContainerProvisioner(_blobServiceClient, _options).ProvisionAsync(AbortToken);

        _provider = _BuildProvider(scoped: true);
        _store = _provider.GetRequiredService<TusAzureStore>();
        _tenant = _provider.GetRequiredService<ICurrentTenant>();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _provider.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_keep_a_tenants_upload_under_the_tenants_folder()
    {
        // given - an upload in progress and a finished, handed-off one
        var inProgress = await _CreateAsync(_Acme, 1_000, Faker.Random.Bytes(300));
        var content = Faker.Random.Bytes(400);
        var finished = await _CreateAsync(_Acme, content.Length, content);

        using (_tenant.Change(_Acme))
        {
            await _store.MarkHandedOffAsync(finished, AbortToken);
        }

        // then - each upload is one blob under the tenant's folder, and nothing sits anywhere else
        var prefix = _options.BlobPrefix;
        (await _ListBlobNamesAsync())
            .Should()
            .BeEquivalentTo($"{prefix}.tenants/acme/{inProgress}", $"{prefix}.tenants/acme/{finished}");

        // and both read back inside the tenant
        using (_tenant.Change(_Acme))
        {
            (await _store.GetUploadOffsetAsync(inProgress, AbortToken)).Should().Be(300);
            (await _ReadAsync(finished)).Should().Equal(content);
            (await _store.GetHandoffStateAsync(finished, AbortToken)).Should().Be(TusHandoffState.HandedOff);
        }
    }

    [Fact]
    public async Task should_hide_a_tenants_upload_from_every_other_tenant()
    {
        // given
        var content = Faker.Random.Bytes(400);
        var fileId = await _CreateAsync(_Acme, content.Length, content);

        // when - another tenant names the id
        using (_tenant.Change(_Globex))
        {
            // then - it reads as an upload that was never created, and deleting it removes nothing
            (await _store.FileExistAsync(fileId, AbortToken))
                .Should()
                .BeFalse();
            (await _store.GetFileAsync(fileId, AbortToken)).Should().BeNull();
            (await _store.GetUploadLengthAsync(fileId, AbortToken)).Should().BeNull();
            (await _store.GetUploadMetadataAsync(fileId, AbortToken)).Should().BeNull();
            (await _store.GetHandoffStateAsync(fileId, AbortToken)).Should().Be(TusHandoffState.NotComplete);
            await _store.DeleteFileAsync(fileId, AbortToken);
        }

        using (_tenant.Change(_Acme))
        {
            (await _store.FileExistAsync(fileId, AbortToken)).Should().BeTrue();
            (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(content.Length);

            // when - its own tenant deletes it
            await _store.DeleteFileAsync(fileId, AbortToken);
        }

        // then
        (await _ListBlobNamesAsync())
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task should_refuse_every_call_made_outside_a_tenant()
    {
        // given
        var fileId = await _CreateAsync(_Acme, 1_000, Faker.Random.Bytes(300));

        // when - no tenant is ambient
        var create = () => _store.CreateFileAsync(1_000, metadata: null, AbortToken);
        var read = () => _store.FileExistAsync(fileId, AbortToken);
        var delete = () => _store.DeleteFileAsync(fileId, AbortToken);

        // then
        await create.Should().ThrowAsync<MissingTenantContextException>();
        await read.Should().ThrowAsync<MissingTenantContextException>();
        await delete.Should().ThrowAsync<MissingTenantContextException>();
        (await _ListBlobNamesAsync()).Should().Equal($"{_options.BlobPrefix}.tenants/acme/{fileId}");
    }

    [Fact]
    public async Task should_remove_the_expired_uploads_of_every_tenant_and_of_none_in_one_pass()
    {
        // given - an expired upload in each of two tenants and one kept outside any tenant, as an upload created
        // before the application scoped its uploads is, plus a live upload and a finished one that must survive
        var past = DateTimeOffset.UtcNow.AddMinutes(-10);
        var unscopedStore = _CreateUnscopedStore();
        var unscoped = await _AppendToNewAsync(unscopedStore, 1_000, Faker.Random.Bytes(300));
        await unscopedStore.SetExpirationAsync(unscoped, past, AbortToken);

        var acme = await _CreateExpiringAsync(_Acme, 1_000, past);
        var globex = await _CreateExpiringAsync(_Globex, 1_000, past);
        var live = await _CreateExpiringAsync(_Globex, 1_000, DateTimeOffset.UtcNow.AddHours(2));
        var finished = await _CreateExpiringAsync(_Acme, 300, past);

        // and another writer's blob under the prefix, in a folder the store never names, carrying a passed expiry
        var foreignBlob = $"{_options.BlobPrefix}.tenants/Not-A-Tenant/{Guid.NewGuid():N}";
        await _Container
            .GetBlobClient(foreignBlob)
            .UploadAsync(
                BinaryData.FromString("data"),
                new BlobUploadOptions
                {
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [TusAzureMetadata.ExpirationKey] = past.ToString("O"),
                    },
                },
                AbortToken
            );

        // when - the pass the cleanup job runs, outside any tenant
        _tenant.Id.Should().BeNull();
        var listed = (await _store.GetExpiredFilesAsync(AbortToken)).ToList();
        var removed = await _store.RemoveExpiredFilesAsync(AbortToken);

        // then
        listed.Should().BeEquivalentTo([unscoped, acme, globex]);
        removed.Should().Be(3);
        var prefix = _options.BlobPrefix;
        (await _ListBlobNamesAsync())
            .Should()
            .BeEquivalentTo($"{prefix}.tenants/globex/{live}", $"{prefix}.tenants/acme/{finished}", foreignBlob);
    }

    [Fact]
    public async Task should_build_a_final_upload_from_partial_uploads_of_the_same_tenant()
    {
        // given
        var first = Faker.Random.Bytes(300);
        var second = Faker.Random.Bytes(500);
        string[] partials;
        string final;
        byte[] built;
        FileConcat? concat;

        // when
        using (_tenant.Change(_Acme))
        {
            partials =
            [
                await _store.CreatePartialFileAsync(first.Length, metadata: null, AbortToken),
                await _store.CreatePartialFileAsync(second.Length, metadata: null, AbortToken),
            ];
            await _AppendAsync(_store, partials[0], first);
            await _AppendAsync(_store, partials[1], second);

            final = await _store.CreateFinalFileAsync(partials, metadata: null, AbortToken);
            built = await _ReadAsync(final);
            concat = await _store.GetUploadConcatAsync(final, AbortToken);
        }

        // then - the final upload is built from the tenant's partial uploads and records their bare ids
        built.Should().Equal([.. first, .. second]);
        concat.Should().BeOfType<FileConcatFinal>().Which.Files.Should().Equal(partials);
        var prefix = _options.BlobPrefix;
        (await _ListBlobNamesAsync())
            .Should()
            .BeEquivalentTo(
                $"{prefix}.tenants/acme/{partials[0]}",
                $"{prefix}.tenants/acme/{partials[1]}",
                $"{prefix}.tenants/acme/{final}"
            );

        // and another tenant cannot build from them
        using (_tenant.Change(_Globex))
        {
            var act = () => _store.CreateFinalFileAsync(partials, metadata: null, AbortToken);
            await act.Should().ThrowAsync<tusdotnet.Models.TusStoreException>();
        }
    }

    [Fact]
    public async Task should_purge_the_uploads_of_every_tenant_and_of_none()
    {
        // given - in a tenant and outside any: a handed-off upload and one in progress; and another writer's blob
        var unscopedStore = _CreateUnscopedStore();
        var content = Faker.Random.Bytes(400);
        var unscopedFinished = await _AppendToNewAsync(unscopedStore, content.Length, content);
        await unscopedStore.MarkHandedOffAsync(unscopedFinished, AbortToken);
        await _AppendToNewAsync(unscopedStore, 1_000, Faker.Random.Bytes(300));

        var scopedFinished = await _CreateAsync(_Acme, content.Length, content);
        await _CreateAsync(_Globex, 1_000, Faker.Random.Bytes(300));

        using (_tenant.Change(_Acme))
        {
            await _store.MarkHandedOffAsync(scopedFinished, AbortToken);
        }

        var foreignBlob = $"{_options.BlobPrefix.TrimEnd('/')}-other/kept";
        await _Container.UploadBlobAsync(foreignBlob, BinaryData.FromString("data"), AbortToken);
        (await _ListBlobNamesAsync())
            .Should()
            .HaveCount(4)
            .And.Contain(name => name.Contains("/.tenants/acme/"))
            .And.Contain(name => name.Contains("/.tenants/globex/"));

        // when - the provisioner the application registered, outside any tenant
        var provisioner = _provider.GetServices<IResourceProvisioner>().Should().ContainSingle().Subject;
        await provisioner.PurgeAsync(AbortToken);

        // then - only the other writer's blob is left
        (await _ListBlobNamesAsync())
            .Should()
            .BeEmpty();
        (await _Container.GetBlobClient(foreignBlob).ExistsAsync(AbortToken)).Value.Should().BeTrue();
    }

    [Fact]
    public async Task should_keep_blob_names_unchanged_when_uploads_are_not_scoped_by_tenant()
    {
        // given - the store an application registers without scoping its uploads by tenant
        await using var provider = _BuildProvider(scoped: false);
        var store = provider.GetRequiredService<TusAzureStore>();

        // when - no tenant is needed
        var fileId = await _AppendToNewAsync(store, 1_000, Faker.Random.Bytes(300));

        // then
        (await _ListBlobNamesAsync())
            .Should()
            .Equal(_options.BlobPrefix + fileId);
        (await store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(300);
    }

    /// <summary>Wired as an application wires it: the store comes from UseAzure and the scoping from the tenancy setup.</summary>
    private ServiceProvider _BuildProvider(bool scoped)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddSingleton(_blobServiceClient);
        builder.Services.AddSingleton(LoggerFactory);
        builder.Services.AddHeadlessTus(tus =>
            tus.UseAzure(options =>
            {
                options.ContainerName = _options.ContainerName;
                options.BlobPrefix = _options.BlobPrefix;
            })
        );

        if (scoped)
        {
            builder.AddHeadlessTenancy(tenancy => tenancy.Tus(tus => tus.ScopeByTenant()));
        }

        return builder.Services.BuildServiceProvider();
    }

    /// <summary>A store an application constructs itself, which keeps uploads outside any tenant.</summary>
    private TusAzureStore _CreateUnscopedStore()
    {
        return new TusAzureStore(_blobServiceClient, _options, loggerFactory: LoggerFactory);
    }

    /// <summary>Creates an upload inside <paramref name="tenantId"/> and appends <paramref name="content"/>.</summary>
    private async Task<string> _CreateAsync(string tenantId, long length, byte[] content)
    {
        using (_tenant.Change(tenantId))
        {
            return await _AppendToNewAsync(_store, length, content);
        }
    }

    /// <summary>
    /// Creates an upload of 300 bytes inside <paramref name="tenantId"/> that expires at <paramref name="expires"/>;
    /// it is incomplete unless <paramref name="length"/> is 300.
    /// </summary>
    private async Task<string> _CreateExpiringAsync(string tenantId, long length, DateTimeOffset expires)
    {
        using (_tenant.Change(tenantId))
        {
            var fileId = await _AppendToNewAsync(_store, length, Faker.Random.Bytes(300));
            await _store.SetExpirationAsync(fileId, expires, AbortToken);

            return fileId;
        }
    }

    private static async Task<string> _AppendToNewAsync(TusAzureStore store, long length, byte[] content)
    {
        var fileId = await store.CreateFileAsync(length, metadata: null, AbortToken);
        await _AppendAsync(store, fileId, content);

        return fileId;
    }

    private static async Task _AppendAsync(TusAzureStore store, string fileId, byte[] content)
    {
        await using var stream = new MemoryStream(content, writable: false);
        await store.AppendDataAsync(fileId, stream, AbortToken);
    }

    /// <summary>Reads the content of an upload of the ambient tenant.</summary>
    private async Task<byte[]> _ReadAsync(string fileId)
    {
        var file = await _store.GetFileAsync(fileId, AbortToken);
        await using var stream = await file!.GetContentAsync(AbortToken);
        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, AbortToken);

        return buffer.ToArray();
    }

    /// <summary>The names of the blobs under the test's prefix.</summary>
    private async Task<List<string>> _ListBlobNamesAsync()
    {
        var names = new List<string>();

        await foreach (
            var blob in _Container.GetBlobsAsync(
                traits: BlobTraits.None,
                states: BlobStates.None,
                prefix: _options.BlobPrefix,
                cancellationToken: AbortToken
            )
        )
        {
            names.Add(blob.Name);
        }

        return names;
    }
}
