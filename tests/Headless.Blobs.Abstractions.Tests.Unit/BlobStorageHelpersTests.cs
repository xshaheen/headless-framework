// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs.Internals;
using Headless.Testing.Tests;

namespace Tests;

public sealed class BlobStorageHelpersTests : TestBase
{
    #region NormalizePath Tests

    [Fact]
    public void should_return_null_for_null_when_normalizing_path()
    {
        // given
        const string? path = null;

        // when
        var result = BlobStorageHelpers.NormalizePath(path);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_preserve_unix_slashes_when_path_uses_forward_slashes()
    {
        // given
        const string path = "a/b/c";

        // when
        var result = BlobStorageHelpers.NormalizePath(path);

        // then
        result.Should().Be("a/b/c");
    }

    [Fact]
    public void should_convert_backslashes_to_forward_when_path_uses_backslashes()
    {
        // given
        const string path = "a\\b\\c";

        // when
        var result = BlobStorageHelpers.NormalizePath(path);

        // then
        result.Should().Be("a/b/c");
    }

    [Fact]
    public void should_handle_mixed_slashes_when_path_has_both_slash_types()
    {
        // given
        const string path = "a/b\\c/d";

        // when
        var result = BlobStorageHelpers.NormalizePath(path);

        // then
        result.Should().Be("a/b/c/d");
    }

    [Fact]
    public void should_preserve_empty_string_when_path_is_empty()
    {
        // given
        const string path = "";

        // when
        var result = BlobStorageHelpers.NormalizePath(path);

        // then
        result.Should().Be("");
    }

    [Fact]
    public void should_handle_single_backslash_when_path_is_single_separator()
    {
        // given
        const string path = "\\";

        // when
        var result = BlobStorageHelpers.NormalizePath(path);

        // then
        result.Should().Be("/");
    }

    #endregion

    #region GetRequestCriteria Tests

    [Fact]
    public void should_return_empty_prefix_for_no_pattern_when_search_pattern_is_null()
    {
        // given
        string[] directories = ["dir"];
        const string? pattern = null;

        // when
        var result = BlobStorageHelpers.GetRequestCriteria(directories, pattern);

        // then
        result.Prefix.Should().Be("dir");
        result.Pattern.Should().BeNull();
    }

    [Fact]
    public void should_combine_directories_and_pattern_when_both_provided()
    {
        // given
        string[] directories = ["a", "b"];
        const string pattern = "*.txt";

        // when
        var result = BlobStorageHelpers.GetRequestCriteria(directories, pattern);

        // then
        result.Prefix.Should().Be("a/b/");
        result.Pattern.Should().NotBeNull();
    }

    [Fact]
    public void should_use_pattern_as_prefix_without_wildcard_when_pattern_has_no_star()
    {
        // given
        string[] directories = ["dir"];
        const string pattern = "file.txt";

        // when
        var result = BlobStorageHelpers.GetRequestCriteria(directories, pattern);

        // then
        result.Prefix.Should().Be("dir/file.txt");
        result.Pattern.Should().BeNull();
    }

    [Fact]
    public void should_extract_prefix_before_wildcard_when_pattern_has_star_in_subpath()
    {
        // given
        string[] directories = ["dir"];
        const string pattern = "sub/*.txt";

        // when
        var result = BlobStorageHelpers.GetRequestCriteria(directories, pattern);

        // then
        result.Prefix.Should().Be("dir/sub/");
        result.Pattern.Should().NotBeNull();
    }

    [Fact]
    public void should_handle_wildcard_in_filename_when_star_in_file_part()
    {
        // given
        string[] directories = ["dir"];
        const string pattern = "file*.txt";

        // when
        var result = BlobStorageHelpers.GetRequestCriteria(directories, pattern);

        // then
        result.Prefix.Should().Be("dir/");
        result.Pattern.Should().NotBeNull();
    }

    [Fact]
    public void should_return_empty_for_empty_directories_and_null_pattern()
    {
        // given
        string[] directories = [];
        const string? pattern = null;

        // when
        var result = BlobStorageHelpers.GetRequestCriteria(directories, pattern);

        // then
        result.Prefix.Should().BeEmpty();
        result.Pattern.Should().BeNull();
    }

    [Fact]
    public void should_handle_backslashes_in_pattern_when_pattern_uses_windows_paths()
    {
        // given
        string[] directories = ["dir"];
        const string pattern = "sub\\*.txt";

        // when
        var result = BlobStorageHelpers.GetRequestCriteria(directories, pattern);

        // then
        result.Prefix.Should().Be("dir/sub/");
        result.Pattern.Should().NotBeNull();
    }

    [Fact]
    public void should_set_prefix_to_empty_when_wildcard_at_start_without_directory()
    {
        // given
        string[] directories = [];
        const string pattern = "*.txt";

        // when
        var result = BlobStorageHelpers.GetRequestCriteria(directories, pattern);

        // then
        result.Prefix.Should().BeEmpty();
        result.Pattern.Should().NotBeNull();
    }

    #endregion

    #region SearchCriteria Regex Tests

    [Theory]
    [InlineData("file.txt", true)]
    [InlineData("document.txt", true)]
    [InlineData("file.json", false)]
    [InlineData("file.txt.bak", false)]
    public void should_match_single_star_pattern_when_checking_txt_files(string testPath, bool shouldMatch)
    {
        // given
        string[] directories = [];
        const string pattern = "*.txt";
        var criteria = BlobStorageHelpers.GetRequestCriteria(directories, pattern);

        // when
        var isMatch = criteria.Pattern!.IsMatch(testPath);

        // then
        isMatch.Should().Be(shouldMatch);
    }

    [Theory]
    [InlineData("dir/sub/file.txt", true)]
    [InlineData("dir/sub/document.txt", true)]
    [InlineData("dir/other/file.txt", false)]
    public void should_match_full_path_pattern_when_checking_directory_prefix(string testPath, bool shouldMatch)
    {
        // given
        string[] directories = ["dir"];
        const string pattern = "sub/*.txt";
        var criteria = BlobStorageHelpers.GetRequestCriteria(directories, pattern);

        // when
        var isMatch = criteria.Pattern!.IsMatch(testPath);

        // then
        isMatch.Should().Be(shouldMatch);
    }

    [Fact]
    public void should_escape_regex_special_chars_when_pattern_contains_brackets()
    {
        // given
        string[] directories = ["dir"];
        const string pattern = "file[1].txt";
        var criteria = BlobStorageHelpers.GetRequestCriteria(directories, pattern);

        // when & then - no wildcard means exact match via prefix, no regex pattern
        criteria.Pattern.Should().BeNull();
        criteria.Prefix.Should().Be("dir/file[1].txt");
    }

    [Fact]
    public void should_match_multiple_stars_when_pattern_has_multiple_wildcards()
    {
        // given
        string[] directories = [];
        const string pattern = "*.test.*.txt";
        var criteria = BlobStorageHelpers.GetRequestCriteria(directories, pattern);

        // when
        var isMatch = criteria.Pattern!.IsMatch("file.test.backup.txt");

        // then
        isMatch.Should().BeTrue();
    }

    [Theory]
    [InlineData("file-a.txt", true)]
    [InlineData("file-.txt", true)]
    [InlineData("file-abc.txt", true)]
    [InlineData("file.txt", false)]
    public void should_match_star_wildcard_pattern_when_checking_various_filenames(string testPath, bool shouldMatch)
    {
        // given
        string[] directories = [];
        const string pattern = "file-*.txt";
        var criteria = BlobStorageHelpers.GetRequestCriteria(directories, pattern);

        // when
        var isMatch = criteria.Pattern!.IsMatch(testPath);

        // then
        isMatch.Should().Be(shouldMatch);
    }

    [Fact]
    public void should_match_star_only_pattern_when_pattern_is_just_star()
    {
        // given
        string[] directories = [];
        const string pattern = "*";
        var criteria = BlobStorageHelpers.GetRequestCriteria(directories, pattern);

        // when & then
        criteria.Pattern!.IsMatch("anything.txt").Should().BeTrue();
        criteria.Pattern.IsMatch("folder/file.txt").Should().BeTrue();
        criteria.Pattern.IsMatch("").Should().BeTrue();
    }

    #endregion

    #region Metadata Key Constants Tests

    [Fact]
    public void should_have_correct_upload_date_metadata_key()
    {
        // then
        BlobStorageHelpers.UploadDateMetadataKey.Should().Be("uploadDate");
    }

    [Fact]
    public void should_have_correct_extension_metadata_key()
    {
        // then
        BlobStorageHelpers.ExtensionMetadataKey.Should().Be("extension");
    }

    #endregion
}
