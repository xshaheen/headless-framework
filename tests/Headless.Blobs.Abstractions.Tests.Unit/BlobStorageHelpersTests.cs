// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.Blobs.Internals;
using Headless.Primitives;
using Headless.Testing.Tests;

namespace Tests;

public sealed class BlobStorageHelpersTests : TestBase
{
    #region NormalizePath Tests

    [Fact]
    public void should_return_null_for_null_when_normalizing_path()
    {
        // Arrange
        const string? path = null;

        // Act
        var result = BlobStorageHelpers.NormalizePath(path);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void should_preserve_unix_slashes_when_path_uses_forward_slashes()
    {
        // Arrange
        const string path = "a/b/c";

        // Act
        var result = BlobStorageHelpers.NormalizePath(path);

        // Assert
        result.Should().Be("a/b/c");
    }

    [Fact]
    public void should_convert_backslashes_to_forward_when_path_uses_backslashes()
    {
        // Arrange
        const string path = "a\\b\\c";

        // Act
        var result = BlobStorageHelpers.NormalizePath(path);

        // Assert
        result.Should().Be("a/b/c");
    }

    [Fact]
    public void should_handle_mixed_slashes_when_path_has_both_slash_types()
    {
        // Arrange
        const string path = "a/b\\c/d";

        // Act
        var result = BlobStorageHelpers.NormalizePath(path);

        // Assert
        result.Should().Be("a/b/c/d");
    }

    [Fact]
    public void should_preserve_empty_string_when_path_is_empty()
    {
        // Arrange
        const string path = "";

        // Act
        var result = BlobStorageHelpers.NormalizePath(path);

        // Assert
        result.Should().Be("");
    }

    [Fact]
    public void should_handle_single_backslash_when_path_is_single_separator()
    {
        // Arrange
        const string path = "\\";

        // Act
        var result = BlobStorageHelpers.NormalizePath(path);

        // Assert
        result.Should().Be("/");
    }

    #endregion

    #region Metadata Key Constants Tests

    [Fact]
    public void should_have_correct_upload_date_metadata_key()
    {
        // Assert
        BlobStorageHelpers.UploadDateMetadataKey.Should().Be("uploadDate");
    }

    [Fact]
    public void should_have_correct_extension_metadata_key()
    {
        // Assert
        BlobStorageHelpers.ExtensionMetadataKey.Should().Be("extension");
    }

    [Fact]
    public void should_detect_reserved_sidecar_suffix_case_insensitively()
    {
        BlobStorageHelpers.IsSidecarKey("report.HLMETA").Should().BeTrue();
    }

    [Fact]
    public void should_detect_reserved_sidecar_suffix_on_any_key_segment()
    {
        BlobStorageHelpers.HasSidecarSegment("folder/report.hlmeta/file.txt").Should().BeTrue();
        BlobStorageHelpers.HasSidecarSegment("folder/report.txt").Should().BeFalse();
    }

    [Fact]
    public void should_strip_framework_metadata_keys_case_insensitively()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["uploaddate"] = "2026-06-27T00:00:00.0000000Z",
            ["EXTENSION"] = ".txt",
            ["author"] = "blake",
        };

        var result = BlobStorageHelpers.ToUserMetadata(metadata);

        result.Should().ContainSingle();
        result!["author"].Should().Be("blake");
    }

    #endregion

    #region Continuation Token Tests

    [Fact]
    public void should_round_trip_continuation_token()
    {
        var token = BlobStorageHelpers.EncodeContinuationToken("folder/file.txt");

        BlobStorageHelpers.DecodeContinuationToken(token).Should().Be("folder/file.txt");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void should_decode_empty_continuation_token_as_null(string? token)
    {
        BlobStorageHelpers.DecodeContinuationToken(token).Should().BeNull();
    }

    #endregion

    #region Delete Counting Tests

    [Fact]
    public void should_count_deleted_bulk_results()
    {
        IReadOnlyCollection<BlobBulkResult> results =
        [
            new(new BlobLocation("container", "a.txt"), Result<bool, Exception>.Ok(true)),
            new(new BlobLocation("container", "b.txt"), Result<bool, Exception>.Ok(false)),
        ];

        BlobStorageHelpers.CountDeletedOrThrow(results, "delete all").Should().Be(1);
    }

    [Fact]
    public void should_throw_when_bulk_delete_results_contain_failures()
    {
        IReadOnlyCollection<BlobBulkResult> results =
        [
            new(new BlobLocation("container", "a.txt"), Result<bool, Exception>.Ok(true)),
            new("container", "../escape.txt", Result<bool, Exception>.Fail(new InvalidOperationException("boom"))),
        ];

        var act = () => BlobStorageHelpers.CountDeletedOrThrow(results, "delete all");

        act.Should().Throw<AggregateException>().WithMessage("*1 blob*");
    }

    #endregion

    #region CreateGlobMatcher Tests

    [Theory]
    [InlineData("*.txt", "file.txt", true)]
    [InlineData("*.txt", "a/b/file.txt", true)]
    [InlineData("*.txt", "file.json", false)]
    [InlineData("file?.txt", "file1.txt", true)]
    [InlineData("file?.txt", "file12.txt", false)]
    [InlineData("file?.txt", "file.txt", false)]
    [InlineData("logs/*.log", "logs/app.log", true)]
    [InlineData("logs/*.log", "other/app.log", false)]
    [InlineData("exact.txt", "exact.txt", true)]
    [InlineData("exact.txt", "exact.txt.bak", false)]
    public void should_match_glob_pattern_against_whole_key(string pattern, string key, bool expected)
    {
        // Arrange
        var matcher = BlobStorageHelpers.CreateGlobMatcher(pattern);

        // Act
        var isMatch = matcher(key);

        // Assert
        isMatch.Should().Be(expected);
    }

    [Fact]
    public void should_treat_regex_special_chars_as_literals_in_glob()
    {
        // Arrange - '.' and '[' are literal in a glob, not regex metacharacters
        var matcher = BlobStorageHelpers.CreateGlobMatcher("file[1].txt");

        // Act & Assert
        matcher("file[1].txt").Should().BeTrue();
        matcher("fileX.txt").Should().BeFalse();
    }

    #endregion

    #region GetLiteralPrefix Tests

    [Theory]
    [InlineData("logs/2024/*.log", "logs/2024/")]
    [InlineData("logs/file?.txt", "logs/file")]
    [InlineData("no-wildcard.txt", "no-wildcard.txt")]
    [InlineData("*.txt", "")]
    [InlineData("?abc", "")]
    [InlineData("a/b/c", "a/b/c")]
    public void should_return_literal_head_before_first_wildcard(string pattern, string expected)
    {
        // Act
        var prefix = BlobStorageHelpers.GetLiteralPrefix(pattern);

        // Assert
        prefix.Should().Be(expected);
    }

    #endregion
}
