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
    public void should_reject_unsafe_file_ids(string fileId)
    {
        var act = () => TusBlobName.Build("uploads/", fileId);

        act.Should().Throw<TusStoreException>().WithMessage($"Invalid TUS file id: '{fileId}'.");
    }
}
