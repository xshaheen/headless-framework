// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Tus.Internal;
using tusdotnet.Models;

namespace Tests.S3;

public sealed class TusS3KeysTests : TestBase
{
    private const string _FileId = "0f8fad5bd9cb469fa165708d4a5e5e7b";

    private readonly TusS3Keys _keys = new("uploads/");
    private readonly TusS3Keys _acmeKeys = new TusS3Keys("uploads/").ForTenant("acme");

    [Fact]
    public void should_place_the_finished_object_directly_under_the_prefix()
    {
        _keys.FinishedObject(_FileId).Should().Be("uploads/" + _FileId);
    }

    [Fact]
    public void should_place_the_working_set_under_the_reserved_tus_folder()
    {
        _keys.WorkingSet(_FileId).Should().Be($"uploads/.tus/{_FileId}/");
        _keys.Info(_FileId).Should().Be($"uploads/.tus/{_FileId}/info");
        _keys.Segments(_FileId).Should().Be($"uploads/.tus/{_FileId}/seg/");
    }

    [Fact]
    public void should_place_the_completed_record_outside_the_working_set()
    {
        _keys.CompletedRecord(_FileId).Should().Be("uploads/.tus-completed/" + _FileId);
    }

    [Fact]
    public void should_place_the_handed_off_mark_in_a_folder_of_its_own()
    {
        _keys.HandedOffMark(_FileId).Should().Be("uploads/.tus-handed-off/" + _FileId);
    }

    [Fact]
    public void should_place_every_key_of_a_tenants_upload_under_the_tenants_folder_of_its_root()
    {
        _acmeKeys.FinishedObject(_FileId).Should().Be($"uploads/.tenants/acme/{_FileId}");
        _acmeKeys.WorkingSet(_FileId).Should().Be($"uploads/.tus/.tenants/acme/{_FileId}/");
        _acmeKeys.Info(_FileId).Should().Be($"uploads/.tus/.tenants/acme/{_FileId}/info");
        _acmeKeys.Segments(_FileId).Should().Be($"uploads/.tus/.tenants/acme/{_FileId}/seg/");
        _acmeKeys
            .Segment(_FileId, 5, "nonce")
            .Should()
            .Be($"uploads/.tus/.tenants/acme/{_FileId}/seg/0000000000000000005-nonce");
        _acmeKeys.CompletedRecord(_FileId).Should().Be($"uploads/.tus-completed/.tenants/acme/{_FileId}");
        _acmeKeys.HandedOffMark(_FileId).Should().Be($"uploads/.tus-handed-off/.tenants/acme/{_FileId}");
    }

    [Fact]
    public void should_root_a_tenants_listings_inside_its_own_folder()
    {
        _acmeKeys.Tenant.Should().Be("acme");
        _acmeKeys.FinishedObjectRoot.Should().Be("uploads/.tenants/acme/");
        _acmeKeys.WorkingSetRoot.Should().Be("uploads/.tus/.tenants/acme/");
    }

    [Fact]
    public void should_root_the_listings_of_uploads_outside_any_tenant_at_the_prefix()
    {
        _keys.Tenant.Should().BeNull();
        _keys.FinishedObjectRoot.Should().Be("uploads/");
        _keys.WorkingSetRoot.Should().Be("uploads/.tus/");
        _keys.TenantWorkingSetsRoot.Should().Be("uploads/.tus/.tenants/");
    }

    [Fact]
    public void should_keep_the_tenants_folder_out_of_reach_of_a_file_id()
    {
        // The folder's own name is the only thing that separates a tenant's keys from an unscoped upload's.
        TusS3Keys.IsValid(TusS3Keys.TenantsFolder.TrimEnd('/')).Should().BeFalse();
    }

    [Fact]
    public void should_keep_a_tenant_named_like_a_file_id_apart_from_the_upload_with_that_id()
    {
        // given - a tenant whose id is also a valid file id
        var tenantKeys = _keys.ForTenant(_FileId);

        // then - the tenant's working sets never sit where the upload's own working set does
        tenantKeys.WorkingSetRoot.Should().Be($"uploads/.tus/.tenants/{_FileId}/");
        tenantKeys.WorkingSetRoot.Should().NotStartWith(_keys.WorkingSet(_FileId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData(".tus")]
    [InlineData(".tus-completed")]
    [InlineData(".tus-handed-off")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("tab\there")]
    public void should_reject_a_file_id_that_could_reach_other_keys(string fileId)
    {
        // when
        var act = () => _keys.Info(fileId);

        // then
        act.Should().Throw<TusStoreException>().WithMessage("Invalid TUS file id*");
    }

    [Fact]
    public void should_reject_a_null_file_id()
    {
        var act = () => TusS3Keys.EnsureValid(null);

        act.Should().Throw<TusStoreException>();
    }

    [Theory]
    [InlineData(_FileId)]
    [InlineData("upload.bin")]
    [InlineData("a-b_c")]
    public void should_accept_a_file_id_that_stays_in_its_own_keys(string fileId)
    {
        TusS3Keys.EnsureValid(fileId).Should().Be(fileId);
    }
}
