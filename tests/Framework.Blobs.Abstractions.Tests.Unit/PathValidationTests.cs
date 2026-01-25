// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Blobs.Internals;
using Framework.Testing.Tests;

namespace Tests;

public sealed class PathValidationTests : TestBase
{
    #region ThrowIfPathTraversal Tests

    [Fact]
    public void should_allow_null_path_when_checking_path_traversal()
    {
        // Arrange
        string? path = null;

        // Act
        var act = () => PathValidation.ThrowIfPathTraversal(path);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void should_allow_empty_path_when_checking_path_traversal()
    {
        // Arrange
        var path = "";

        // Act
        var act = () => PathValidation.ThrowIfPathTraversal(path);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void should_allow_simple_path_when_checking_path_traversal()
    {
        // Arrange
        var path = "folder/file.txt";

        // Act
        var act = () => PathValidation.ThrowIfPathTraversal(path);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void should_throw_for_unix_traversal_when_path_contains_parent_directory()
    {
        // Arrange
        var path = "../secret.txt";

        // Act
        var act = () => PathValidation.ThrowIfPathTraversal(path);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*traversal*");
    }

    [Fact]
    public void should_throw_for_windows_traversal_when_path_contains_backslash_parent()
    {
        // Arrange
        var path = "..\\secret.txt";

        // Act
        var act = () => PathValidation.ThrowIfPathTraversal(path);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*traversal*");
    }

    [Fact]
    public void should_throw_for_mid_path_unix_traversal_when_path_contains_embedded_parent()
    {
        // Arrange
        var path = "folder/../secret.txt";

        // Act
        var act = () => PathValidation.ThrowIfPathTraversal(path);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*traversal*");
    }

    [Fact]
    public void should_throw_for_mid_path_windows_traversal_when_path_contains_embedded_backslash_parent()
    {
        // Arrange
        var path = "folder\\..\\secret.txt";

        // Act
        var act = () => PathValidation.ThrowIfPathTraversal(path);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*traversal*");
    }

    [Fact]
    public void should_throw_for_trailing_traversal_unix_when_path_ends_with_parent()
    {
        // Arrange
        var path = "folder/..";

        // Act
        var act = () => PathValidation.ThrowIfPathTraversal(path);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*traversal*");
    }

    [Fact]
    public void should_throw_for_trailing_traversal_windows_when_path_ends_with_backslash_parent()
    {
        // Arrange
        var path = "folder\\..";

        // Act
        var act = () => PathValidation.ThrowIfPathTraversal(path);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*traversal*");
    }

    [Fact]
    public void should_throw_for_starts_with_double_dot_when_path_begins_with_parent()
    {
        // Arrange
        var path = "..";

        // Act
        var act = () => PathValidation.ThrowIfPathTraversal(path);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*traversal*");
    }

    [Fact]
    public void should_allow_single_dot_when_path_contains_current_directory()
    {
        // Arrange
        var path = "./file.txt";

        // Act
        var act = () => PathValidation.ThrowIfPathTraversal(path);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void should_allow_dotted_filename_when_file_has_double_dots_in_name()
    {
        // Arrange
        var path = "file..name.txt";

        // Act
        var act = () => PathValidation.ThrowIfPathTraversal(path);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void should_include_param_name_in_exception_when_path_is_invalid()
    {
        // Arrange
        var badPath = "../secret";

        // Act
        var act = () => PathValidation.ThrowIfPathTraversal(badPath);

        // Assert
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("badPath");
    }

    [Theory]
    [InlineData("a/../b")]
    [InlineData("a/..\\b")]
    [InlineData("a\\../b")]
    [InlineData("..\\a")]
    [InlineData("a/b/..")]
    [InlineData("a\\b\\..")]
    public void should_throw_for_various_traversal_patterns_when_path_contains_parent_directory(string path)
    {
        // Act
        var act = () => PathValidation.ThrowIfPathTraversal(path);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region ThrowIfAbsolutePath Tests

    [Fact]
    public void should_allow_null_path_when_checking_absolute_path()
    {
        // Arrange
        string? path = null;

        // Act
        var act = () => PathValidation.ThrowIfAbsolutePath(path);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void should_allow_empty_path_when_checking_absolute_path()
    {
        // Arrange
        var path = "";

        // Act
        var act = () => PathValidation.ThrowIfAbsolutePath(path);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void should_allow_relative_path_when_checking_absolute_path()
    {
        // Arrange
        var path = "folder/file.txt";

        // Act
        var act = () => PathValidation.ThrowIfAbsolutePath(path);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void should_throw_for_unix_absolute_when_path_starts_with_forward_slash()
    {
        // Arrange
        var path = "/etc/passwd";

        // Act
        var act = () => PathValidation.ThrowIfAbsolutePath(path);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*Absolute*");
    }

    [Fact]
    public void should_throw_for_windows_absolute_when_path_starts_with_backslash()
    {
        // Arrange
        var path = "\\Windows\\system32";

        // Act
        var act = () => PathValidation.ThrowIfAbsolutePath(path);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*Absolute*");
    }

    [Fact]
    public void should_allow_path_with_mid_slash_when_slash_is_not_at_start()
    {
        // Arrange
        var path = "folder/subfolder";

        // Act
        var act = () => PathValidation.ThrowIfAbsolutePath(path);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void should_include_param_name_in_absolute_path_exception_when_path_is_absolute()
    {
        // Arrange
        var absolutePath = "/root/secret";

        // Act
        var act = () => PathValidation.ThrowIfAbsolutePath(absolutePath);

        // Assert
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("absolutePath");
    }

    #endregion

    #region ThrowIfControlCharacters Tests

    [Fact]
    public void should_allow_null_path_when_checking_control_characters()
    {
        // Arrange
        string? path = null;

        // Act
        var act = () => PathValidation.ThrowIfControlCharacters(path);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void should_allow_empty_path_when_checking_control_characters()
    {
        // Arrange
        var path = "";

        // Act
        var act = () => PathValidation.ThrowIfControlCharacters(path);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void should_allow_normal_characters_when_path_has_standard_chars()
    {
        // Arrange
        var path = "folder/file.txt";

        // Act
        var act = () => PathValidation.ThrowIfControlCharacters(path);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void should_throw_for_null_char_when_path_contains_null_byte()
    {
        // Arrange
        var path = "file\0.txt";

        // Act
        var act = () => PathValidation.ThrowIfControlCharacters(path);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*Control*");
    }

    [Fact]
    public void should_throw_for_newline_when_path_contains_line_feed()
    {
        // Arrange
        var path = "file\n.txt";

        // Act
        var act = () => PathValidation.ThrowIfControlCharacters(path);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*Control*");
    }

    [Fact]
    public void should_throw_for_carriage_return_when_path_contains_cr()
    {
        // Arrange
        var path = "file\r.txt";

        // Act
        var act = () => PathValidation.ThrowIfControlCharacters(path);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*Control*");
    }

    [Fact]
    public void should_throw_for_tab_when_path_contains_horizontal_tab()
    {
        // Arrange
        var path = "file\t.txt";

        // Act
        var act = () => PathValidation.ThrowIfControlCharacters(path);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*Control*");
    }

    [Fact]
    public void should_throw_for_bell_when_path_contains_bell_character()
    {
        // Arrange
        var path = "file\x07.txt";

        // Act
        var act = () => PathValidation.ThrowIfControlCharacters(path);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*Control*");
    }

    [Fact]
    public void should_allow_space_when_path_contains_space_character()
    {
        // Arrange
        var path = "file name.txt";

        // Act
        var act = () => PathValidation.ThrowIfControlCharacters(path);

        // Assert
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("\x00")] // Null
    [InlineData("\x01")] // Start of Heading
    [InlineData("\x1F")] // Unit Separator (char 31, just below space)
    public void should_throw_for_various_control_chars_when_path_contains_low_ascii(string controlChar)
    {
        // Arrange
        var path = $"file{controlChar}.txt";

        // Act
        var act = () => PathValidation.ThrowIfControlCharacters(path);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region ValidatePathSegment Tests

    [Fact]
    public void should_combine_all_validations_when_validating_path_segment()
    {
        // Arrange - path with traversal
        var pathWithTraversal = "../secret";

        // Act
        var act = () => PathValidation.ValidatePathSegment(pathWithTraversal);

        // Assert - should throw for traversal
        act.Should().Throw<ArgumentException>().WithMessage("*traversal*");
    }

    [Fact]
    public void should_validate_absolute_path_in_segment_when_path_is_absolute()
    {
        // Arrange
        var absolutePath = "/root";

        // Act
        var act = () => PathValidation.ValidatePathSegment(absolutePath);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*Absolute*");
    }

    [Fact]
    public void should_validate_control_chars_in_segment_when_path_has_control_chars()
    {
        // Arrange
        var pathWithControl = "file\t.txt";

        // Act
        var act = () => PathValidation.ValidatePathSegment(pathWithControl);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*Control*");
    }

    [Fact]
    public void should_allow_valid_segment_when_path_is_clean()
    {
        // Arrange
        var validPath = "folder/subfolder/file.txt";

        // Act
        var act = () => PathValidation.ValidatePathSegment(validPath);

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    #region ValidateContainer Tests

    [Fact]
    public void should_validate_all_segments_when_validating_container()
    {
        // Arrange
        string[] container = ["folder", "subfolder"];

        // Act
        var act = () => PathValidation.ValidateContainer(container);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void should_throw_on_first_invalid_segment_when_container_has_bad_segment()
    {
        // Arrange
        string[] container = ["folder", "../secret", "another"];

        // Act
        var act = () => PathValidation.ValidateContainer(container);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*traversal*");
    }

    [Fact]
    public void should_allow_valid_container_when_all_segments_are_valid()
    {
        // Arrange
        string[] container = ["bucket", "users", "uploads"];

        // Act
        var act = () => PathValidation.ValidateContainer(container);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void should_throw_for_absolute_segment_in_container_when_segment_starts_with_slash()
    {
        // Arrange
        string[] container = ["folder", "/absolute"];

        // Act
        var act = () => PathValidation.ValidateContainer(container);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*Absolute*");
    }

    [Fact]
    public void should_throw_for_control_char_in_container_when_segment_has_control_char()
    {
        // Arrange
        string[] container = ["folder", "bad\0name"];

        // Act
        var act = () => PathValidation.ValidateContainer(container);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*Control*");
    }

    [Fact]
    public void should_allow_empty_container_when_array_is_empty()
    {
        // Arrange
        string[] container = [];

        // Act
        var act = () => PathValidation.ValidateContainer(container);

        // Assert
        act.Should().NotThrow();
    }

    #endregion
}
