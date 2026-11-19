// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Tus.Internal;
using tusdotnet.Models;

namespace Tests.Azure;

public sealed class TusBlobNameTests
{
    [Fact]
    public void should_build_blob_name_from_prefix_and_file_id()
    {
        TusBlobName.Build("uploads/", "abc123").Should().Be("uploads/abc123");
    }

    [Fact]
    public void should_append_slash_to_prefix_when_missing()
    {
        TusBlobName.Build("uploads", "abc123").Should().Be("uploads/abc123");
    }

    [Fact]
    public void should_accept_guid_provider_style_ids()
    {
        var id = Guid.NewGuid().ToString("N");

        TusBlobName.Build("uploads/", id).Should().Be($"uploads/{id}");
    }

    [Fact]
    public void should_accept_ids_with_inner_spaces()
    {
        // Inner whitespace round-trips through the metadata id lists (TrimEntries only trims ends).
        TusBlobName.Build("uploads/", "a b").Should().Be("uploads/a b");
    }

    [Theory]
    [InlineData("")] // empty
    [InlineData("..")] // traversal
    [InlineData("a/../b")] // traversal
    [InlineData("a/b")] // path separator escapes the prefix
    [InlineData("a\\b")] // path separator escapes the prefix
    [InlineData("a\tb")] // control character
    [InlineData("a\nb")] // control character
    [InlineData("a,b")] // ',' separates file-id lists in blob metadata (tus_partial_uploads)
    [InlineData(" abc")] // leading whitespace corrupts TrimEntries round-trips
    [InlineData("abc ")] // trailing whitespace corrupts TrimEntries round-trips + Azure hazard
    [InlineData(".tenants")] // the folder that holds each tenant's uploads
    [InlineData(".abc")] // a leading dot is reserved for the store's own folders
    public void should_reject_unsafe_file_ids(string fileId)
    {
        var act = () => TusBlobName.Build("uploads/", fileId);

        act.Should().Throw<TusStoreException>().WithMessage($"Invalid TUS file id: '{fileId}'.");
    }

    [Theory]
    [InlineData("uploads/")]
    [InlineData("uploads")]
    public void should_build_a_tenants_blob_name_under_the_tenants_folder(string blobPrefix)
    {
        TusBlobName.Build(blobPrefix, "abc123", tenant: "acme").Should().Be("uploads/.tenants/acme/abc123");
    }

    [Fact]
    public void should_reject_unsafe_file_ids_inside_a_tenant()
    {
        var act = () => TusBlobName.Build("uploads/", "../globex/abc123", tenant: "acme");

        act.Should().Throw<TusStoreException>();
    }

    [Theory]
    [InlineData("uploads/", "uploads/abc123", null, "abc123")]
    [InlineData("uploads", "uploads/abc123", null, "abc123")]
    [InlineData("uploads/", "uploads/a b", null, "a b")]
    [InlineData("uploads/", "uploads/.tenants/acme/abc123", "acme", "abc123")]
    [InlineData("uploads", "uploads/.tenants/acme/abc123", "acme", "abc123")]
    [InlineData("uploads/", "uploads/.tenants/a.b_c-1/abc123", "a.b_c-1", "abc123")]
    // A tenant id with the shape of a file id is still read as the tenant.
    [InlineData(
        "uploads/",
        "uploads/.tenants/0f8fad5bd9cb469fa16570867728950e/abc123",
        "0f8fad5bd9cb469fa16570867728950e",
        "abc123"
    )]
    public void should_split_a_blob_name_into_the_tenant_and_file_id_it_was_built_from(
        string blobPrefix,
        string blobName,
        string? expectedTenant,
        string expectedFileId
    )
    {
        // when
        var split = TusBlobName.TrySplit(blobPrefix, blobName, out var tenant, out var fileId);

        // then
        split.Should().BeTrue();
        tenant.Should().Be(expectedTenant);
        fileId.Should().Be(expectedFileId);
        TusBlobName.Build(blobPrefix, fileId!, tenant).Should().Be(blobName);
    }

    [Theory]
    [InlineData("other/abc123")] // outside the prefix
    [InlineData("uploads-other/abc123")] // a sibling folder that only shares the prefix's letters
    [InlineData("uploads/")] // no file id
    [InlineData("uploads/sub/abc123")] // a folder the store does not write
    [InlineData("uploads/.hidden")] // a leading dot is never a file id
    [InlineData("uploads/.tenants")] // the tenants folder itself
    [InlineData("uploads/.tenants/acme")] // a tenant with no upload
    [InlineData("uploads/.tenants/acme/")] // a tenant with no file id
    [InlineData("uploads/.tenants//abc123")] // no tenant
    [InlineData("uploads/.tenants/Acme/abc123")] // not a tenant id the store accepts
    [InlineData("uploads/.tenants/acme/sub/abc123")] // a folder the store does not write inside a tenant
    [InlineData("uploads/.tenants/acme/.tenants")] // a leading dot is never a file id inside a tenant either
    public void should_not_split_a_blob_name_the_store_could_not_have_built(string blobName)
    {
        // when
        var split = TusBlobName.TrySplit("uploads/", blobName, out var tenant, out var fileId);

        // then
        split.Should().BeFalse();
        tenant.Should().BeNull();
        fileId.Should().BeNull();
    }
}
