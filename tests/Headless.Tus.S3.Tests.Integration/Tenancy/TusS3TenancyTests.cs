// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3.Model;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Headless.Tus;
using Headless.Tus.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Tests.TestSetup;

namespace Tests.Tenancy;

/// <summary>
/// With uploads scoped by tenant, the store an application registers keeps every object of an upload under its
/// tenant's folder, serves an upload only inside its tenant, and still removes expired uploads and purges the
/// store from outside any tenant. Each test owns a key prefix, because a sweep and a purge cover their whole
/// prefix.
/// </summary>
public abstract class TusS3TenancyTestsBase(IS3TestBackend backend) : TestBase
{
    private const int _MiB = 1024 * 1024;
    private const string _Acme = "acme";
    private const string _Globex = "globex";

    private readonly S3TestClient _client = backend.CreateClient();
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);

    private readonly TusS3StoreOptions _options = new()
    {
        Bucket = TusS3StoreFactory.Bucket,
        Prefix = $"tenancy-{Guid.NewGuid():N}/",
        PartSize = 5 * _MiB,
    };

    private ServiceProvider _provider = null!;
    private TusS3Store _store = null!;
    private ICurrentTenant _tenant = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _options.Provider = _client.Provider;
        await new TusS3BucketProvisioner(_client.S3, _options, LoggerFactory).ProvisionAsync(AbortToken);

        // Wired as an application wires it: the store comes from UseS3 and the scoping from the tenancy setup.
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddSingleton(_client.S3);
        builder.Services.AddSingleton(LoggerFactory);
        builder.Services.AddSingleton<TimeProvider>(_clock);
        builder.Services.AddHeadlessTus(tus =>
            tus.UseS3(options =>
            {
                options.Bucket = _options.Bucket;
                options.Prefix = _options.Prefix;
                options.Provider = _options.Provider;
                options.PartSize = _options.PartSize;
            })
        );
        builder.AddHeadlessTenancy(tenancy => tenancy.Tus(tus => tus.ScopeByTenant()));

        _provider = builder.Services.BuildServiceProvider();
        _store = _provider.GetRequiredService<TusS3Store>();
        _tenant = _provider.GetRequiredService<ICurrentTenant>();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _provider.DisposeAsync();
        _client.Dispose();
        await base.DisposeAsyncCore();
    }

    public virtual async Task should_keep_every_object_of_a_tenants_uploads_under_the_tenants_folder()
    {
        // given - an upload in progress that holds a part and a tail segment, and a finished, handed-off upload
        var inProgress = await _CreateAsync(_Acme, 20 * _MiB, Faker.Random.Bytes(6 * _MiB));
        var content = Faker.Random.Bytes(400);
        var finished = await _CreateAsync(_Acme, content.Length, content);

        using (_tenant.Change(_Acme))
        {
            await _store.MarkHandedOffAsync(finished, AbortToken);
        }

        // then - each kind of object sits under the tenant's folder inside its own root
        var prefix = _options.Prefix;
        var keys = await _ListKeysAsync(prefix);
        keys.Should().Contain($"{prefix}.tus/.tenants/acme/{inProgress}/info");
        keys.Should().Contain(key => key.StartsWith($"{prefix}.tus/.tenants/acme/{inProgress}/seg/"));
        keys.Should().Contain($"{prefix}.tenants/acme/{finished}");
        keys.Should().Contain($"{prefix}.tus-completed/.tenants/acme/{finished}");
        keys.Should().Contain($"{prefix}.tus-handed-off/.tenants/acme/{finished}");

        // and nothing of either upload sits anywhere else, the parts of the one in progress included
        keys.Should().OnlyContain(key => key.Contains("/.tenants/acme/"));
        (await _ListMultipartUploadIdsAsync($"{prefix}.tenants/acme/{inProgress}")).Should().ContainSingle();
        (await _ListMultipartUploadIdsAsync(prefix + inProgress)).Should().BeEmpty();

        // and the finished upload reads back inside its tenant
        using (_tenant.Change(_Acme))
        {
            var file = await _store.GetFileAsync(finished, AbortToken);
            await using var stream = await file!.GetContentAsync(AbortToken);
            await using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, AbortToken);
            buffer.ToArray().Should().Equal(content);
            (await _store.GetHandoffStateAsync(finished, AbortToken)).Should().Be(TusHandoffState.HandedOff);
        }
    }

    public virtual async Task should_hide_a_tenants_upload_from_every_other_tenant()
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
            (await _store.GetUploadMetadataAsync(fileId, AbortToken)).Should().BeNull();
            (await _store.GetHandoffStateAsync(fileId, AbortToken)).Should().Be(TusHandoffState.NotComplete);
            await _store.DeleteFileAsync(fileId, AbortToken);
        }

        using (_tenant.Change(_Acme))
        {
            (await _store.FileExistAsync(fileId, AbortToken)).Should().BeTrue();
            (await _store.GetUploadOffsetAsync(fileId, AbortToken)).Should().Be(content.Length);
        }
    }

    public virtual async Task should_refuse_every_call_made_outside_a_tenant()
    {
        // given
        var fileId = await _CreateAsync(_Acme, 1000, Faker.Random.Bytes(300));

        // when - no tenant is ambient
        var create = () => _store.CreateFileAsync(1000, metadata: null, AbortToken);
        var read = () => _store.FileExistAsync(fileId, AbortToken);
        var delete = () => _store.DeleteFileAsync(fileId, AbortToken);

        // then
        await create.Should().ThrowAsync<MissingTenantContextException>();
        await read.Should().ThrowAsync<MissingTenantContextException>();
        await delete.Should().ThrowAsync<MissingTenantContextException>();
        (await _ListKeysAsync(_options.Prefix)).Should().OnlyContain(key => key.Contains("/.tenants/acme/"));
    }

    public virtual async Task should_remove_a_tenants_expired_upload_and_abort_its_multipart_upload_with_no_ambient_tenant()
    {
        // given - an expired upload of tenant acme that holds a part and a tail segment, and a live one
        var expired = await _CreateAsync(_Acme, 20 * _MiB, Faker.Random.Bytes(6 * _MiB));
        var live = await _CreateAsync(_Acme, 1000, Faker.Random.Bytes(300));

        using (_tenant.Change(_Acme))
        {
            await _store.SetExpirationAsync(expired, _clock.GetUtcNow().AddMinutes(10), AbortToken);
            await _store.SetExpirationAsync(live, _clock.GetUtcNow().AddHours(2), AbortToken);
        }

        var expiredKey = $"{_options.Prefix}.tenants/acme/{expired}";
        (await _ListMultipartUploadIdsAsync(expiredKey)).Should().ContainSingle();
        _clock.Advance(TimeSpan.FromMinutes(30));

        // when - the pass the cleanup job runs, outside any tenant
        _tenant.Id.Should().BeNull();
        var listed = (await _store.GetExpiredFilesAsync(AbortToken)).ToList();
        var removed = await _store.RemoveExpiredFilesAsync(AbortToken);

        // then - the expired upload is gone with every object and part it owned
        listed.Should().Equal(expired);
        removed.Should().Be(1);
        (await _ListKeysAsync(_options.Prefix)).Should().NotContain(key => key.Contains(expired));
        (await _ListMultipartUploadIdsAsync(expiredKey)).Should().BeEmpty();

        // and the live one is untouched
        using (_tenant.Change(_Acme))
        {
            (await _store.FileExistAsync(expired, AbortToken)).Should().BeFalse();
            (await _store.GetUploadOffsetAsync(live, AbortToken)).Should().Be(300);
        }
    }

    public virtual async Task should_remove_the_expired_uploads_of_every_tenant_and_of_none_in_one_pass()
    {
        // given - an expired upload in each of two tenants and one kept outside any tenant, as an upload created
        // before the application scoped its uploads is, plus a live upload that must survive
        var unscopedStore = _CreateUnscopedStore();
        var unscoped = await _AppendToNewAsync(unscopedStore, 1000, Faker.Random.Bytes(300));
        await unscopedStore.SetExpirationAsync(unscoped, _clock.GetUtcNow().AddMinutes(10), AbortToken);

        var acme = await _CreateExpiringAsync(_Acme, TimeSpan.FromMinutes(10));
        var globex = await _CreateExpiringAsync(_Globex, TimeSpan.FromMinutes(10));
        var live = await _CreateExpiringAsync(_Globex, TimeSpan.FromHours(2));
        _clock.Advance(TimeSpan.FromMinutes(30));

        // when
        var listed = (await _store.GetExpiredFilesAsync(AbortToken)).ToList();
        var removed = await _store.RemoveExpiredFilesAsync(AbortToken);

        // then
        listed.Should().BeEquivalentTo([unscoped, acme, globex]);
        removed.Should().Be(3);
        (await _ListKeysAsync(_options.Prefix))
            .Should()
            .OnlyContain(key => key.StartsWith($"{_options.Prefix}.tus/.tenants/globex/{live}/"));
    }

    public virtual async Task should_sweep_a_tenant_whose_id_has_the_shape_of_a_file_id()
    {
        // given - a tenant id that is also a valid file id, so its folder could be mistaken for a working set
        var tenantId = Guid.NewGuid().ToString("N");
        var expired = await _CreateExpiringAsync(tenantId, TimeSpan.FromMinutes(10));
        _clock.Advance(TimeSpan.FromMinutes(30));

        // when
        var removed = await _store.RemoveExpiredFilesAsync(AbortToken);

        // then
        removed.Should().Be(1);
        (await _ListKeysAsync(_options.Prefix)).Should().NotContain(key => key.Contains(expired));
    }

    public virtual async Task should_judge_a_multipart_upload_only_against_the_uploads_of_its_own_tenant()
    {
        // given - a live upload outside any tenant and a live one in tenant acme, each with the multipart upload its
        // state records, plus one unrecorded multipart upload left on the tenant upload's key
        var unscopedStore = _CreateUnscopedStore();
        var unscoped = await _AppendToNewAsync(unscopedStore, 20 * _MiB, Faker.Random.Bytes(6 * _MiB));
        await unscopedStore.SetExpirationAsync(unscoped, _clock.GetUtcNow().AddDays(1), AbortToken);
        var unscopedKey = _options.Prefix + unscoped;
        var unscopedRecorded = (await _ListMultipartUploadIdsAsync(unscopedKey)).Should().ContainSingle().Subject;

        var scoped = await _CreateAsync(_Acme, 20 * _MiB, Faker.Random.Bytes(6 * _MiB));

        using (_tenant.Change(_Acme))
        {
            await _store.SetExpirationAsync(scoped, _clock.GetUtcNow().AddDays(1), AbortToken);
        }

        var scopedKey = $"{_options.Prefix}.tenants/acme/{scoped}";
        var scopedRecorded = (await _ListMultipartUploadIdsAsync(scopedKey)).Should().ContainSingle().Subject;
        var scopedLeak = await _InitiateMultipartUploadAsync(scopedKey);

        // when - a pass once every multipart upload is older than the grace period
        _clock.Advance(TimeSpan.FromMinutes(90));
        var removed = await _store.RemoveExpiredFilesAsync(AbortToken);

        // then - the pass over the uploads kept outside any tenant left the tenant's multipart upload alone, and the
        // tenant's own pass aborted only the one its upload does not record
        removed.Should().Be(0);
        (await _ListMultipartUploadIdsAsync(unscopedKey)).Should().Equal(unscopedRecorded);
        (await _ListMultipartUploadIdsAsync(scopedKey)).Should().Equal(scopedRecorded);
        scopedLeak.Should().NotBe(scopedRecorded);

        // and both uploads still take bytes into the parts they hold
        using (_tenant.Change(_Acme))
        {
            await _AppendAsync(_store, scoped, Faker.Random.Bytes(5 * _MiB));
            (await _store.GetUploadOffsetAsync(scoped, AbortToken)).Should().Be(11 * _MiB);
        }

        await _AppendAsync(unscopedStore, unscoped, Faker.Random.Bytes(5 * _MiB));
        (await unscopedStore.GetUploadOffsetAsync(unscoped, AbortToken)).Should().Be(11 * _MiB);
    }

    public virtual async Task should_delete_a_tenants_upload_with_every_object_it_owns()
    {
        // given - a handed-off upload and one in progress with a part
        var content = Faker.Random.Bytes(400);
        var finished = await _CreateAsync(_Acme, content.Length, content);
        var inProgress = await _CreateAsync(_Acme, 20 * _MiB, Faker.Random.Bytes(6 * _MiB));

        // when
        using (_tenant.Change(_Acme))
        {
            await _store.MarkHandedOffAsync(finished, AbortToken);
            await _store.DeleteFileAsync(finished, AbortToken);
            await _store.DeleteFileAsync(inProgress, AbortToken);
        }

        // then
        (await _ListKeysAsync(_options.Prefix))
            .Should()
            .BeEmpty();
        (await _ListMultipartUploadIdsAsync($"{_options.Prefix}.tenants/acme/{inProgress}")).Should().BeEmpty();
    }

    public virtual async Task should_build_a_final_upload_from_partial_uploads_of_the_same_tenant()
    {
        // given
        var first = Faker.Random.Bytes(300);
        var second = Faker.Random.Bytes(500);
        byte[] built;
        string final;

        // when
        using (_tenant.Change(_Acme))
        {
            var partials = new[]
            {
                await _store.CreatePartialFileAsync(first.Length, metadata: null, AbortToken),
                await _store.CreatePartialFileAsync(second.Length, metadata: null, AbortToken),
            };
            await _AppendAsync(_store, partials[0], first);
            await _AppendAsync(_store, partials[1], second);

            final = await _store.CreateFinalFileAsync(partials, metadata: null, AbortToken);

            var file = await _store.GetFileAsync(final, AbortToken);
            await using var stream = await file!.GetContentAsync(AbortToken);
            await using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, AbortToken);
            built = buffer.ToArray();
        }

        // then
        built.Should().Equal([.. first, .. second]);
        (await _ListKeysAsync(_options.Prefix)).Should().OnlyContain(key => key.Contains("/.tenants/acme/"));
        (await _ListKeysAsync(_options.Prefix)).Should().Contain($"{_options.Prefix}.tenants/acme/{final}");
    }

    public virtual async Task should_purge_the_uploads_of_every_tenant_and_of_none_with_their_marks()
    {
        // given - in a tenant and outside any: a handed-off upload and one in progress; and another writer's object
        var unscopedStore = _CreateUnscopedStore();
        var content = Faker.Random.Bytes(400);
        var unscopedFinished = await _AppendToNewAsync(unscopedStore, content.Length, content);
        await unscopedStore.MarkHandedOffAsync(unscopedFinished, AbortToken);
        await _AppendToNewAsync(unscopedStore, 1000, Faker.Random.Bytes(300));

        var scopedFinished = await _CreateAsync(_Acme, content.Length, content);
        await _CreateAsync(_Globex, 1000, Faker.Random.Bytes(300));

        using (_tenant.Change(_Acme))
        {
            await _store.MarkHandedOffAsync(scopedFinished, AbortToken);
        }

        var foreignKey = _options.Prefix + "another-writer-object";
        await _PutAsync(foreignKey, Faker.Random.Bytes(10));
        (await _ListKeysAsync(_options.Prefix)).Should().HaveCountGreaterThan(8);

        // when - the provisioner the application registered, outside any tenant
        var provisioner = _provider
            .GetServices<Headless.Hosting.IResourceProvisioner>()
            .Should()
            .ContainSingle()
            .Subject;
        await provisioner.PurgeAsync(AbortToken);

        // then - only the other writer's object is left
        (await _ListKeysAsync(_options.Prefix))
            .Should()
            .Equal(foreignKey);
    }

    /// <summary>A store an application constructs itself, which keeps uploads outside any tenant.</summary>
    private TusS3Store _CreateUnscopedStore()
    {
        return new TusS3Store(_client.S3, _options, loggerFactory: LoggerFactory, timeProvider: _clock);
    }

    /// <summary>Creates an upload inside <paramref name="tenantId"/> and appends <paramref name="content"/>.</summary>
    private async Task<string> _CreateAsync(string tenantId, long length, byte[] content)
    {
        using (_tenant.Change(tenantId))
        {
            return await _AppendToNewAsync(_store, length, content);
        }
    }

    /// <summary>Creates an incomplete upload inside <paramref name="tenantId"/> that expires after <paramref name="expiresIn"/>.</summary>
    private async Task<string> _CreateExpiringAsync(string tenantId, TimeSpan expiresIn)
    {
        using (_tenant.Change(tenantId))
        {
            var fileId = await _AppendToNewAsync(_store, 1000, Faker.Random.Bytes(300));
            await _store.SetExpirationAsync(fileId, _clock.GetUtcNow().Add(expiresIn), AbortToken);

            return fileId;
        }
    }

    private static async Task<string> _AppendToNewAsync(TusS3Store store, long length, byte[] content)
    {
        var fileId = await store.CreateFileAsync(length, metadata: null, AbortToken);
        await _AppendAsync(store, fileId, content);

        return fileId;
    }

    private static async Task _AppendAsync(TusS3Store store, string fileId, byte[] content)
    {
        await using var stream = new MemoryStream(content);
        await store.AppendDataAsync(fileId, stream, AbortToken);
    }

    private async Task _PutAsync(string key, byte[] content)
    {
        await using var body = new MemoryStream(content);
        var request = new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            InputStream = body,
            AutoCloseStream = false,
        };
        TusS3RequestPresets.Apply(request, _client.Provider);

        await _client.S3.PutObjectAsync(request, AbortToken);
    }

    private async Task<string> _InitiateMultipartUploadAsync(string key)
    {
        var response = await _client.S3.InitiateMultipartUploadAsync(
            new InitiateMultipartUploadRequest { BucketName = _options.Bucket, Key = key },
            AbortToken
        );

        return response.UploadId;
    }

    private async Task<List<string>> _ListKeysAsync(string prefix)
    {
        var response = await _client.S3.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = _options.Bucket, Prefix = prefix },
            AbortToken
        );

        return [.. (response.S3Objects ?? []).Select(o => o.Key)];
    }

    private async Task<List<string>> _ListMultipartUploadIdsAsync(string key)
    {
        var response = await _client.S3.ListMultipartUploadsAsync(
            new ListMultipartUploadsRequest { BucketName = _options.Bucket, Prefix = key },
            AbortToken
        );

        return
        [
            .. (response.MultipartUploads ?? [])
                .Where(u => string.Equals(u.Key, key, StringComparison.Ordinal))
                .Select(u => u.UploadId),
        ];
    }
}

[Collection<TusS3MinioFixture>]
public sealed class MinioTusS3TenancyTests(TusS3MinioFixture fixture) : TusS3TenancyTestsBase(fixture)
{
    [Fact]
    public override Task should_keep_every_object_of_a_tenants_uploads_under_the_tenants_folder() =>
        base.should_keep_every_object_of_a_tenants_uploads_under_the_tenants_folder();

    [Fact]
    public override Task should_hide_a_tenants_upload_from_every_other_tenant() =>
        base.should_hide_a_tenants_upload_from_every_other_tenant();

    [Fact]
    public override Task should_refuse_every_call_made_outside_a_tenant() =>
        base.should_refuse_every_call_made_outside_a_tenant();

    [Fact]
    public override Task should_remove_a_tenants_expired_upload_and_abort_its_multipart_upload_with_no_ambient_tenant() =>
        base.should_remove_a_tenants_expired_upload_and_abort_its_multipart_upload_with_no_ambient_tenant();

    [Fact]
    public override Task should_remove_the_expired_uploads_of_every_tenant_and_of_none_in_one_pass() =>
        base.should_remove_the_expired_uploads_of_every_tenant_and_of_none_in_one_pass();

    [Fact]
    public override Task should_sweep_a_tenant_whose_id_has_the_shape_of_a_file_id() =>
        base.should_sweep_a_tenant_whose_id_has_the_shape_of_a_file_id();

    // No leak scenario here: MinIO lists multipart uploads only under an exact object key, so the sweep's listing
    // over a whole prefix finds none, for a tenant as for the uploads kept outside one.

    [Fact]
    public override Task should_delete_a_tenants_upload_with_every_object_it_owns() =>
        base.should_delete_a_tenants_upload_with_every_object_it_owns();

    [Fact]
    public override Task should_build_a_final_upload_from_partial_uploads_of_the_same_tenant() =>
        base.should_build_a_final_upload_from_partial_uploads_of_the_same_tenant();

    [Fact]
    public override Task should_purge_the_uploads_of_every_tenant_and_of_none_with_their_marks() =>
        base.should_purge_the_uploads_of_every_tenant_and_of_none_with_their_marks();
}

[Collection<TusS3LocalStackFixture>]
public sealed class LocalStackTusS3TenancyTests(TusS3LocalStackFixture fixture) : TusS3TenancyTestsBase(fixture)
{
    [Fact]
    public override Task should_keep_every_object_of_a_tenants_uploads_under_the_tenants_folder() =>
        base.should_keep_every_object_of_a_tenants_uploads_under_the_tenants_folder();

    [Fact]
    public override Task should_hide_a_tenants_upload_from_every_other_tenant() =>
        base.should_hide_a_tenants_upload_from_every_other_tenant();

    [Fact]
    public override Task should_refuse_every_call_made_outside_a_tenant() =>
        base.should_refuse_every_call_made_outside_a_tenant();

    [Fact]
    public override Task should_remove_a_tenants_expired_upload_and_abort_its_multipart_upload_with_no_ambient_tenant() =>
        base.should_remove_a_tenants_expired_upload_and_abort_its_multipart_upload_with_no_ambient_tenant();

    [Fact]
    public override Task should_remove_the_expired_uploads_of_every_tenant_and_of_none_in_one_pass() =>
        base.should_remove_the_expired_uploads_of_every_tenant_and_of_none_in_one_pass();

    [Fact]
    public override Task should_sweep_a_tenant_whose_id_has_the_shape_of_a_file_id() =>
        base.should_sweep_a_tenant_whose_id_has_the_shape_of_a_file_id();

    [Fact]
    public override Task should_judge_a_multipart_upload_only_against_the_uploads_of_its_own_tenant() =>
        base.should_judge_a_multipart_upload_only_against_the_uploads_of_its_own_tenant();

    [Fact]
    public override Task should_delete_a_tenants_upload_with_every_object_it_owns() =>
        base.should_delete_a_tenants_upload_with_every_object_it_owns();

    [Fact]
    public override Task should_build_a_final_upload_from_partial_uploads_of_the_same_tenant() =>
        base.should_build_a_final_upload_from_partial_uploads_of_the_same_tenant();

    [Fact]
    public override Task should_purge_the_uploads_of_every_tenant_and_of_none_with_their_marks() =>
        base.should_purge_the_uploads_of_every_tenant_and_of_none_with_their_marks();
}
