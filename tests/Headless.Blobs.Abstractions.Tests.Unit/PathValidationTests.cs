// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs.Internal;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PathValidationTests : TestBase
{
    #region ThrowIfPathTraversal Tests

    [Fact]
    public void should_allow_null_path_when_checking_path_traversal()
    {
        // given
        const string? path = null;

        // when
        var act = () => PathValidation.ThrowIfPathTraversal(path);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_allow_empty_path_when_checking_path_traversal()
    {
        // given
        const string path = "";

        // when
        var act = () => PathValidation.ThrowIfPathTraversal(path);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_allow_simple_path_when_checking_path_traversal()
    {
        // given
        const string path = "folder/file.txt";

        // when
        var act = () => PathValidation.ThrowIfPathTraversal(path);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_allow_single_dot_when_path_contains_current_directory()
    {
        // given
        const string path = "./file.txt";

        // when
        var act = () => PathValidation.ThrowIfPathTraversal(path);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_allow_dotted_filename_when_file_has_double_dots_in_name()
    {
        // given
        const string path = "file..name.txt";

        // when
        var act = () => PathValidation.ThrowIfPathTraversal(path);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_include_param_name_in_exception_when_path_is_invalid()
    {
        // given
        const string badPath = "../secret";

        // when
        var act = () => PathValidation.ThrowIfPathTraversal(badPath);

        // then
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("badPath");
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("..\\secret.txt")]
    [InlineData("folder/../secret.txt")]
    [InlineData("folder\\..\\secret.txt")]
    [InlineData("folder/..")]
    [InlineData("folder\\..")]
    [InlineData("..")]
    [InlineData("a/../b")]
    [InlineData("a/..\\b")]
    [InlineData("a\\../b")]
    [InlineData("..\\a")]
    [InlineData("a/b/..")]
    [InlineData("a\\b\\..")]
    public void should_throw_for_traversal_patterns_when_path_contains_parent_directory(string path)
    {
        // when
        var act = () => PathValidation.ThrowIfPathTraversal(path);

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*traversal*");
    }

    #endregion

    #region ThrowIfAbsolutePath Tests

    [Fact]
    public void should_allow_null_path_when_checking_absolute_path()
    {
        // given
        const string? path = null;

        // when
        var act = () => PathValidation.ThrowIfAbsolutePath(path);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_allow_empty_path_when_checking_absolute_path()
    {
        // given
        const string path = "";

        // when
        var act = () => PathValidation.ThrowIfAbsolutePath(path);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_allow_relative_path_when_checking_absolute_path()
    {
        // given
        const string path = "folder/file.txt";

        // when
        var act = () => PathValidation.ThrowIfAbsolutePath(path);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_throw_for_unix_absolute_when_path_starts_with_forward_slash()
    {
        // given
        const string path = "/etc/passwd";

        // when
        var act = () => PathValidation.ThrowIfAbsolutePath(path);

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*Absolute*");
    }

    [Fact]
    public void should_throw_for_windows_absolute_when_path_starts_with_backslash()
    {
        // given
        const string path = "\\Windows\\system32";

        // when
        var act = () => PathValidation.ThrowIfAbsolutePath(path);

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*Absolute*");
    }

    [Fact]
    public void should_allow_path_with_mid_slash_when_slash_is_not_at_start()
    {
        // given
        const string path = "folder/subfolder";

        // when
        var act = () => PathValidation.ThrowIfAbsolutePath(path);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_include_param_name_in_absolute_path_exception_when_path_is_absolute()
    {
        // given
        const string absolutePath = "/root/secret";

        // when
        var act = () => PathValidation.ThrowIfAbsolutePath(absolutePath);

        // then
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("absolutePath");
    }

    #endregion

    #region ThrowIfControlCharacters Tests

    [Fact]
    public void should_allow_null_path_when_checking_control_characters()
    {
        // given
        const string? path = null;

        // when
        var act = () => PathValidation.ThrowIfControlCharacters(path);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_allow_empty_path_when_checking_control_characters()
    {
        // given
        const string path = "";

        // when
        var act = () => PathValidation.ThrowIfControlCharacters(path);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_allow_normal_characters_when_path_has_standard_chars()
    {
        // given
        const string path = "folder/file.txt";

        // when
        var act = () => PathValidation.ThrowIfControlCharacters(path);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_allow_space_when_path_contains_space_character()
    {
        // given
        const string path = "file name.txt";

        // when
        var act = () => PathValidation.ThrowIfControlCharacters(path);

        // then
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("\0")] // Null
    [InlineData("\x01")] // Start of Heading
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\t")]
    [InlineData("\a")]
    [InlineData("\x1F")] // Unit Separator (char 31, just below space)
    public void should_throw_for_control_chars_when_path_contains_low_ascii(string controlChar)
    {
        // given
        var path = $"file{controlChar}.txt";

        // when
        var act = () => PathValidation.ThrowIfControlCharacters(path);

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*Control*");
    }

    #endregion

    #region ValidatePathSegment Tests

    [Fact]
    public void should_combine_all_validations_when_validating_path_segment()
    {
        // given - path with traversal
        const string pathWithTraversal = "../secret";

        // when
        var act = () => PathValidation.ValidatePathSegment(pathWithTraversal);

        // then - should throw for traversal
        act.Should().Throw<ArgumentException>().WithMessage("*traversal*");
    }

    [Fact]
    public void should_validate_absolute_path_in_segment_when_path_is_absolute()
    {
        // given
        const string absolutePath = "/root";

        // when
        var act = () => PathValidation.ValidatePathSegment(absolutePath);

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*Absolute*");
    }

    [Fact]
    public void should_validate_control_chars_in_segment_when_path_has_control_chars()
    {
        // given
        const string pathWithControl = "file\t.txt";

        // when
        var act = () => PathValidation.ValidatePathSegment(pathWithControl);

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*Control*");
    }

    [Fact]
    public void should_allow_valid_segment_when_path_is_clean()
    {
        // given
        const string validPath = "folder/subfolder/file.txt";

        // when
        var act = () => PathValidation.ValidatePathSegment(validPath);

        // then
        act.Should().NotThrow();
    }

    #endregion

    #region ValidateContainer Tests

    [Fact]
    public void should_validate_all_segments_when_validating_container()
    {
        // given
        string[] container = ["folder", "subfolder"];

        // when
        var act = () => PathValidation.ValidateContainer(container);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_throw_on_first_invalid_segment_when_container_has_bad_segment()
    {
        // given
        string[] container = ["folder", "../secret", "another"];

        // when
        var act = () => PathValidation.ValidateContainer(container);

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*traversal*");
    }

    [Fact]
    public void should_allow_valid_container_when_all_segments_are_valid()
    {
        // given
        string[] container = ["bucket", "users", "uploads"];

        // when
        var act = () => PathValidation.ValidateContainer(container);

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_throw_for_absolute_segment_in_container_when_segment_starts_with_slash()
    {
        // given
        string[] container = ["folder", "/absolute"];

        // when
        var act = () => PathValidation.ValidateContainer(container);

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*Absolute*");
    }

    [Fact]
    public void should_throw_for_control_char_in_container_when_segment_has_control_char()
    {
        // given
        string[] container = ["folder", "bad\0name"];

        // when
        var act = () => PathValidation.ValidateContainer(container);

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*Control*");
    }

    [Fact]
    public void should_allow_empty_container_when_array_is_empty()
    {
        // given
        string[] container = [];

        // when
        var act = () => PathValidation.ValidateContainer(container);

        // then
        act.Should().NotThrow();
    }

    #endregion
}
