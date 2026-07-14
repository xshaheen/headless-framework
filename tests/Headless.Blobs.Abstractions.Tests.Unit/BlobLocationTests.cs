// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.Blobs.Internals;
using Headless.Testing.Tests;

namespace Tests;

public sealed class BlobLocationTests : TestBase
{
    [Fact]
    public void should_expose_container_and_path_when_valid()
    {
        // Act
        var location = new BlobLocation("bucket", "folder/file.txt");

        // Assert
        location.Container.Should().Be("bucket");
        location.Path.Should().Be("folder/file.txt");
    }

    [Fact]
    public void should_join_segments_with_slash_when_using_params_ctor()
    {
        // Act
        var location = new BlobLocation("bucket", "a", "b", "c.txt");

        // Assert
        location.Path.Should().Be("a/b/c.txt");
        location.Container.Should().Be("bucket");
    }

    [Fact]
    public void should_treat_first_segment_as_container_when_using_segments_only_ctor()
    {
        // Act
        var location = new BlobLocation(["bucket", "a", "b", "c.txt"]);

        // Assert
        location.Container.Should().Be("bucket");
        location.Path.Should().Be("a/b/c.txt");
    }

    [Fact]
    public void should_accept_array_when_using_segments_only_ctor()
    {
        // Arrange
        string[] segments = ["bucket", "folder", "file.txt"];

        // Act
        var location = new BlobLocation(segments);

        // Assert
        location.Container.Should().Be("bucket");
        location.Path.Should().Be("folder/file.txt");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void should_throw_when_segments_only_ctor_has_fewer_than_two_segments(int length)
    {
        // Arrange
        var segments = new string[length];
        Array.Fill(segments, "bucket");

        // Act
        var act = () => new BlobLocation(segments);

        // Assert
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("segments");
    }

    [Fact]
    public void should_prefer_segments_only_ctor_for_all_string_arguments()
    {
        // Act — without overload-resolution priority this would bind to the (container, params) ctor
        // and throw for the empty joined path with ParamName "path" instead.
        var act = () => new BlobLocation("bucket");

        // Assert
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("segments");
    }

    [Fact]
    public void should_validate_path_when_using_segments_only_ctor()
    {
        // Act
        var act = () => new BlobLocation(["bucket", "..", "secret.txt"]);

        // Assert
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("path");
    }

    [Fact]
    public void should_use_value_equality()
    {
        // Assert
        new BlobLocation("b", "p")
            .Should()
            .Be(new BlobLocation("b", "p"));
        new BlobLocation("b", "p").Should().NotBe(new BlobLocation("b", "q"));
        new BlobLocation("b", "p").Should().NotBe(new BlobLocation("c", "p"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_throw_when_container_is_missing(string? container)
    {
        // Act
        var act = () => new BlobLocation(container!, "file.txt");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_throw_when_path_is_missing(string? path)
    {
        // Act
        var act = () => new BlobLocation("bucket", path!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("folder/../secret.txt")]
    [InlineData("a/b/..")]
    public void should_throw_when_path_has_traversal(string path)
    {
        // Act
        var act = () => new BlobLocation("bucket", path);

        // Assert
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("path");
    }

    [Fact]
    public void should_throw_when_path_is_absolute()
    {
        // Act
        var act = () => new BlobLocation("bucket", "/etc/passwd");

        // Assert
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("path");
    }

    [Fact]
    public void should_throw_when_path_has_control_characters()
    {
        // Act
        var act = () => new BlobLocation("bucket", "file\0.txt");

        // Assert
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("path");
    }

    [Fact]
    public void should_throw_when_container_has_traversal()
    {
        // Act
        var act = () => new BlobLocation("..", "file.txt");

        // Assert
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("container");
    }

    [Fact]
    public void should_throw_when_path_matches_reserved_sidecar_suffix()
    {
        // Act
        var act = () => new BlobLocation("bucket", "report" + BlobStorageHelpers.SidecarSuffix);

        // Assert
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("path");
    }

    [Fact]
    public void should_throw_when_path_segment_matches_reserved_sidecar_suffix()
    {
        // Act
        var act = () => new BlobLocation("bucket", "report.hlmeta/content.txt");

        // Assert
        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("path");
    }

    [Fact]
    public void should_allow_dotted_filename_when_not_traversal()
    {
        // Act
        var act = () => new BlobLocation("bucket", "file..name.txt");

        // Assert
        act.Should().NotThrow();
    }
}
