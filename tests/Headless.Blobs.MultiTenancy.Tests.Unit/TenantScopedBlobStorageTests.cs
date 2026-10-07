// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.MultiTenancy;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>Behavior of the tenant-scoped blob storage wrapper, built through <c>tenancy.Blobs(b => b.ScopeByTenant())</c>.</summary>
public sealed class TenantScopedBlobStorageTests : TestBase
{
    private const string _Tenant = "acme";
    private static readonly BlobLocation _Avatar = new("avatars", "1.png");
    private static readonly BlobLocation _ScopedAvatar = new("avatars", "acme/1.png");

    private readonly IBlobStorage _inner = Substitute.For<IBlobStorage, IPresignedUrlBlobStorage>();
    private ServiceProvider? _provider;

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }

        await base.DisposeAsyncCore();
    }

    #region Path prefix

    [Fact]
    public async Task should_prefix_the_path_with_the_tenant_when_uploading()
    {
        // given
        var (storage, tenant) = _Build();
        await using var content = new MemoryStream([1, 2, 3]);

        // when
        using (tenant.Change(_Tenant))
        {
            await storage.UploadAsync(_Avatar, content, contentType: "image/png", cancellationToken: AbortToken);
        }

        // then
        await _inner.Received(1).UploadAsync(_ScopedAvatar, content, "image/png", null, AbortToken);
    }

    [Fact]
    public async Task should_scope_the_location_and_strip_the_file_name_when_downloading()
    {
        // given
        var (storage, tenant) = _Build();
        var stream = new MemoryStream([1]);
        await using var innerResult = new BlobDownloadResult(stream, "acme/1.png");
        _inner.OpenReadStreamAsync(_ScopedAvatar, AbortToken).Returns(innerResult);

        // when
        BlobDownloadResult? result;

        using (tenant.Change(_Tenant))
        {
            result = await storage.OpenReadStreamAsync(_Avatar, AbortToken);
        }

        // then
        await using var _ = result;
        result.Should().NotBeNull();
        result!.FileName.Should().Be("1.png");
        result.Stream.Should().BeSameAs(stream);
    }

    [Fact]
    public async Task should_return_null_when_the_scoped_blob_is_missing()
    {
        // given
        var (storage, tenant) = _Build();
        _inner.OpenReadStreamAsync(_ScopedAvatar, AbortToken).Returns((BlobDownloadResult?)null);

        // when
        BlobDownloadResult? result;

        using (tenant.Change(_Tenant))
        {
            result = await storage.OpenReadStreamAsync(_Avatar, AbortToken);
        }

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_scope_the_location_when_checking_existence()
    {
        // given
        var (storage, tenant) = _Build();
        _inner.ExistsAsync(_ScopedAvatar, AbortToken).Returns(true);

        // when
        bool exists;

        using (tenant.Change(_Tenant))
        {
            exists = await storage.ExistsAsync(_Avatar, AbortToken);
        }

        // then
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task should_strip_the_tenant_from_the_key_when_reading_blob_info()
    {
        // given
        var (storage, tenant) = _Build();
        _inner.GetBlobInfoAsync(_ScopedAvatar, AbortToken).Returns(_Info("acme/1.png"));

        // when
        BlobInfo? info;

        using (tenant.Change(_Tenant))
        {
            info = await storage.GetBlobInfoAsync(_Avatar, AbortToken);
        }

        // then
        info!.BlobKey.Should().Be("1.png");
        info.Size.Should().Be(10);
    }

    [Theory]
    [InlineData(null, "acme/")]
    [InlineData("2026/", "acme/2026/")]
    public async Task should_scope_the_prefix_and_strip_keys_when_listing(string? prefix, string scopedPrefix)
    {
        // given
        var (storage, tenant) = _Build();
        var query = new BlobQuery("avatars", prefix, pageSize: 7, continuationToken: "token", includeMetadata: true);
        BlobQuery? forwarded = null;
        _inner
            .ListAsync(Arg.Do<BlobQuery>(q => forwarded = q), AbortToken)
            .Returns(new BlobPage([_Info("acme/2026/a.png")], "next"));

        // when
        BlobPage page;

        using (tenant.Change(_Tenant))
        {
            page = await storage.ListAsync(query, AbortToken);
        }

        // then
        forwarded.Should().Be(new BlobQuery("avatars", scopedPrefix, 7, "token", includeMetadata: true));
        page.Items.Select(i => i.BlobKey).Should().Equal("2026/a.png");
        page.ContinuationToken.Should().Be("next");
    }

    [Fact]
    public async Task should_refuse_a_listed_key_outside_the_tenant_prefix()
    {
        // given
        var (storage, tenant) = _Build();
        _inner.ListAsync(Arg.Any<BlobQuery>(), AbortToken).Returns(new BlobPage([_Info("globex/1.png")], null));

        // when
        var action = async () =>
        {
            using (tenant.Change(_Tenant))
            {
                await storage.ListAsync(new BlobQuery("avatars"), AbortToken);
            }
        };

        // then
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*outside the ambient tenant*");
    }

    [Fact]
    public async Task should_delete_only_the_tenant_prefix_when_deleting_all()
    {
        // given
        var (storage, tenant) = _Build();
        _inner.DeleteAllAsync(new BlobQuery("avatars", "acme/"), AbortToken).Returns(3);

        // when
        int deleted;

        using (tenant.Change(_Tenant))
        {
            deleted = await storage.DeleteAllAsync(new BlobQuery("avatars"), AbortToken);
        }

        // then
        deleted.Should().Be(3);
    }

    [Fact]
    public async Task should_scope_the_location_when_deleting()
    {
        // given
        var (storage, tenant) = _Build();
        _inner.DeleteAsync(_ScopedAvatar, AbortToken).Returns(true);

        // when
        bool deleted;

        using (tenant.Change(_Tenant))
        {
            deleted = await storage.DeleteAsync(_Avatar, AbortToken);
        }

        // then
        deleted.Should().BeTrue();
    }

    [Fact]
    public async Task should_scope_source_and_destination_when_moving_and_copying()
    {
        // given
        var (storage, tenant) = _Build();
        var destination = new BlobLocation("archive", "old/1.png");
        var scopedDestination = new BlobLocation("archive", "acme/old/1.png");
        _inner.MoveAsync(_ScopedAvatar, scopedDestination, AbortToken).Returns(true);
        _inner.CopyAsync(_ScopedAvatar, scopedDestination, AbortToken).Returns(true);

        // when
        bool moved,
            copied;

        using (tenant.Change(_Tenant))
        {
            moved = await storage.MoveAsync(_Avatar, destination, AbortToken);
            copied = await storage.CopyAsync(_Avatar, destination, AbortToken);
        }

        // then
        moved.Should().BeTrue();
        copied.Should().BeTrue();
    }

    [Fact]
    public async Task should_scope_paths_and_restore_identity_when_bulk_uploading()
    {
        // given
        var (storage, tenant) = _Build();
        await using var content = new MemoryStream();
        IReadOnlyCollection<BlobUploadRequest>? forwarded = null;
        _inner
            .BulkUploadAsync("avatars", Arg.Do<IReadOnlyCollection<BlobUploadRequest>>(r => forwarded = r), AbortToken)
            .Returns([new BlobBulkResult(new BlobLocation("avatars", "acme/1.png"), Result<bool, Exception>.Ok(true))]);

        // when
        IReadOnlyList<BlobBulkResult> results;

        using (tenant.Change(_Tenant))
        {
            results = await storage.BulkUploadAsync(
                "avatars",
                [new BlobUploadRequest("1.png", content), new BlobUploadRequest("../escape.png", content)],
                AbortToken
            );
        }

        // then
        forwarded!.Select(r => r.Path).Should().Equal("acme/1.png");
        results.Should().HaveCount(2);
        var uploaded = results.Single(r => r.Result.IsSuccess);
        uploaded.Location.Should().Be(new BlobLocation("avatars", "1.png"));
        uploaded.Path.Should().Be("1.png");
        var rejected = results.Single(r => r.Result.IsFailure);
        rejected.Path.Should().Be("../escape.png");
        rejected.Location.Should().BeNull();
        rejected.Result.Error.Should().BeOfType<ArgumentException>();
    }

    [Fact]
    public async Task should_not_turn_an_empty_path_into_the_tenant_root_when_bulk_deleting()
    {
        // given
        var (storage, tenant) = _Build();
        IReadOnlyCollection<string>? forwarded = null;
        _inner
            .BulkDeleteAsync("avatars", Arg.Do<IReadOnlyCollection<string>>(p => forwarded = p), AbortToken)
            .Returns([new BlobBulkResult("avatars", "acme/1.png", Result<bool, Exception>.Ok(false))]);

        // when
        IReadOnlyList<BlobBulkResult> results;

        using (tenant.Change(_Tenant))
        {
            results = await storage.BulkDeleteAsync("avatars", ["1.png", " "], AbortToken);
        }

        // then
        forwarded.Should().Equal("acme/1.png");
        results.Should().ContainSingle(r => r.Path == "1.png" && r.Result.IsSuccess && !r.Result.Value);
        results.Should().ContainSingle(r => r.Path == " " && r.Result.IsFailure);
    }

    [Fact]
    public async Task should_not_call_the_store_when_every_bulk_entry_is_invalid()
    {
        // given
        var (storage, tenant) = _Build();

        // when
        IReadOnlyList<BlobBulkResult> results;

        using (tenant.Change(_Tenant))
        {
            results = await storage.BulkDeleteAsync("avatars", ["../a"], AbortToken);
        }

        // then
        results.Should().ContainSingle(r => r.Result.IsFailure);
        await _inner.DidNotReceiveWithAnyArgs().BulkDeleteAsync(default!, default!, AbortToken);
    }

    [Fact]
    public async Task should_scope_the_location_when_presigning()
    {
        // given
        var (storage, tenant) = _Build();
        var presigned = (IPresignedUrlBlobStorage)_inner;
        var expiry = TimeSpan.FromMinutes(5);
        var constraints = new PresignedUploadConstraints { ContentType = "image/png" };
        presigned.GetPresignedDownloadUrlAsync(_ScopedAvatar, expiry, AbortToken).Returns(new Uri("https://d"));
        presigned
            .GetPresignedUploadUrlAsync(_ScopedAvatar, expiry, constraints, AbortToken)
            .Returns(new Uri("https://u"));
        presigned.SupportedUploadConstraints.Returns(PresignedUploadConstraintKinds.ContentType);

        // when
        Uri download,
            upload;

        using (tenant.Change(_Tenant))
        {
            var scoped = (IPresignedUrlBlobStorage)storage;
            download = await scoped.GetPresignedDownloadUrlAsync(_Avatar, expiry, AbortToken);
            upload = await scoped.GetPresignedUploadUrlAsync(_Avatar, expiry, constraints, AbortToken);
        }

        // then
        download.Should().Be(new Uri("https://d"));
        upload.Should().Be(new Uri("https://u"));
        ((IPresignedUrlBlobStorage)storage)
            .SupportedUploadConstraints.Should()
            .Be(PresignedUploadConstraintKinds.ContentType);
    }

    [Fact]
    public void should_not_claim_presign_when_the_wrapped_store_cannot_presign()
    {
        // given
        var inner = Substitute.For<IBlobStorage>();

        // when
        var (storage, _) = _Build(inner);

        // then
        storage.Should().NotBeAssignableTo<IPresignedUrlBlobStorage>();
    }

    #endregion

    #region Fail closed

    [Fact]
    public async Task should_refuse_the_operation_when_no_tenant_is_set()
    {
        // given
        var (storage, _) = _Build();

        // when
        var action = () => storage.ExistsAsync(_Avatar, AbortToken).AsTask();

        // then
        await action.Should().ThrowAsync<MissingTenantContextException>();
        await _inner.DidNotReceiveWithAnyArgs().ExistsAsync(default, AbortToken);
    }

    [Fact]
    public async Task should_refuse_a_list_when_no_tenant_is_set()
    {
        // given
        var (storage, _) = _Build();

        // when
        var action = () => storage.DeleteAllAsync(new BlobQuery("avatars"), AbortToken).AsTask();

        // then
        await action.Should().ThrowAsync<MissingTenantContextException>();
        await _inner.DidNotReceiveWithAnyArgs().DeleteAllAsync(default!, AbortToken);
    }

    [Fact]
    public async Task should_expose_the_wrapped_store_as_unscoped()
    {
        // given
        var (storage, _) = _Build();
        _inner.ExistsAsync(_Avatar, AbortToken).Returns(true);

        // when
        var unscoped = storage.Should().BeAssignableTo<IScopedBlobStorage>().Subject.Unscoped;
        var exists = await unscoped.ExistsAsync(_Avatar, AbortToken);

        // then
        unscoped.Should().BeSameAs(_inner);
        exists.Should().BeTrue("the unscoped store needs no tenant and receives the location unchanged");
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a:b")]
    [InlineData("..")]
    [InlineData(".hidden")]
    [InlineData("acme.hlmeta")]
    [InlineData("é")]
    [InlineData("Acme")]
    [InlineData("0190C7A2-7A5E-7B3C-9F2E-1D4C5B6A7E8F")]
    public async Task should_refuse_a_tenant_id_that_is_not_one_safe_path_segment(string tenantId)
    {
        // given
        var (storage, tenant) = _Build();

        // when
        var action = async () =>
        {
            using (tenant.Change(tenantId))
            {
                await storage.ExistsAsync(_Avatar, AbortToken);
            }
        };

        // then
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*path segment*");
        await _inner.DidNotReceiveWithAnyArgs().ExistsAsync(default, AbortToken);
    }

    [Theory]
    [InlineData("0190c7a2-7a5e-7b3c-9f2e-1d4c5b6a7e8f")]
    [InlineData("42")]
    [InlineData("acme_corp.eu")]
    public async Task should_accept_common_tenant_id_shapes(string tenantId)
    {
        // given
        var (storage, tenant) = _Build();

        // when
        using (tenant.Change(tenantId))
        {
            await storage.ExistsAsync(_Avatar, AbortToken);
        }

        // then
        await _inner.Received(1).ExistsAsync(new BlobLocation("avatars", $"{tenantId}/1.png"), AbortToken);
    }

    #endregion

    #region Container per tenant

    [Fact]
    public async Task should_use_the_tenant_container_and_prefix_the_logical_container_when_container_per_tenant()
    {
        // given
        var (storage, tenant) = _BuildContainerPerTenant();
        await using var content = new MemoryStream();

        // when
        using (tenant.Change(_Tenant))
        {
            await storage.UploadAsync(_Avatar, content, cancellationToken: AbortToken);
        }

        // then
        await _inner
            .Received(1)
            .UploadAsync(new BlobLocation("app-acme", "avatars/1.png"), content, null, null, AbortToken);
    }

    [Fact]
    public async Task should_scope_the_query_to_the_tenant_container_when_container_per_tenant()
    {
        // given
        var (storage, tenant) = _BuildContainerPerTenant();
        _inner
            .ListAsync(new BlobQuery("app-acme", "avatars/2026/"), AbortToken)
            .Returns(new BlobPage([_Info("avatars/2026/a.png")], null));

        // when
        BlobPage page;

        using (tenant.Change(_Tenant))
        {
            page = await storage.ListAsync(new BlobQuery("avatars", "2026/"), AbortToken);
        }

        // then
        page.Items.Select(i => i.BlobKey).Should().Equal("2026/a.png");
    }

    [Fact]
    public async Task should_restore_the_logical_container_in_bulk_results_when_container_per_tenant()
    {
        // given
        var (storage, tenant) = _BuildContainerPerTenant();
        _inner
            .BulkDeleteAsync("app-acme", Arg.Any<IReadOnlyCollection<string>>(), AbortToken)
            .Returns([
                new BlobBulkResult(new BlobLocation("app-acme", "avatars/1.png"), Result<bool, Exception>.Ok(true)),
            ]);

        // when
        IReadOnlyList<BlobBulkResult> results;

        using (tenant.Change(_Tenant))
        {
            results = await storage.BulkDeleteAsync("avatars", ["1.png"], AbortToken);
        }

        // then
        results.Should().ContainSingle().Which.Location.Should().Be(_Avatar);
    }

    [Theory]
    [InlineData("Acme")]
    [InlineData("a--b")]
    [InlineData("-acme")]
    [InlineData("a_b")]
    [InlineData("0190c7a2-7a5e-7b3c-9f2e-1d4c5b6a7e8f-0190c7a2-7a5e-7b3c-9f2e")]
    public async Task should_refuse_a_tenant_id_that_cannot_name_a_container(string tenantId)
    {
        // given
        var (storage, tenant) = _BuildContainerPerTenant();

        // when
        var action = async () =>
        {
            using (tenant.Change(tenantId))
            {
                await storage.ExistsAsync(_Avatar, AbortToken);
            }
        };

        // then
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*name a container*");
    }

    [Fact]
    public async Task should_refuse_a_container_with_a_separator_when_container_per_tenant()
    {
        // given
        var (storage, tenant) = _BuildContainerPerTenant();

        // when
        var action = async () =>
        {
            using (tenant.Change(_Tenant))
            {
                await storage.ExistsAsync(new BlobLocation("a/b", "c.png"), AbortToken);
            }
        };

        // then
        await action.Should().ThrowAsync<ArgumentException>().WithParameterName("container");
    }

    #endregion

    private (IBlobStorage Storage, ICurrentTenant Tenant) _Build(
        IBlobStorage? inner = null,
        Action<TenantBlobScopingOptions>? configure = null
    )
    {
        var store = inner ?? _inner;
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddHeadlessBlobs(blobs => blobs.RegisterDefaultProvider(s => s.AddSingleton(store)));
        builder.AddHeadlessTenancy(tenancy => tenancy.Blobs(b => b.ScopeByTenant(configure ?? (static _ => { }))));

        _provider = builder.Services.BuildServiceProvider();

        return (_provider.GetRequiredService<IBlobStorage>(), _provider.GetRequiredService<ICurrentTenant>());
    }

    private (IBlobStorage Storage, ICurrentTenant Tenant) _BuildContainerPerTenant()
    {
        return _Build(configure: options =>
        {
            options.Strategy = TenantBlobScopingStrategy.ContainerPerTenant;
            options.ContainerPrefix = "app-";
        });
    }

    private static BlobInfo _Info(string key)
    {
        return new BlobInfo
        {
            BlobKey = key,
            Created = DateTimeOffset.UnixEpoch,
            Modified = DateTimeOffset.UnixEpoch,
            Size = 10,
        };
    }
}
