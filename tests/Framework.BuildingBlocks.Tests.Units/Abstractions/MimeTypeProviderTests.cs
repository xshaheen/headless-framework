// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks.Abstractions;

namespace Tests.Abstractions;

public sealed class MimeTypeProviderTests
{
    private readonly MimeTypeProvider _mimeTypeProvider = new();

    [Fact]
    public void get_mime_type_should_return_correct_mime_type_for_known_file_extension()
    {
        // given
        const string fileName = "example.txt";

        // when
        var mimeType = _mimeTypeProvider.GetMimeType(fileName);

        // then
        mimeType.Should().Be("text/plain");
    }

    [Fact]
    public void get_mime_type_should_return_octet_stream_for_unknown_file_extension()
    {
        // given
        const string fileName = "unknownfile.unknown";

        // when
        var mimeType = _mimeTypeProvider.GetMimeType(fileName);

        // then
        mimeType.Should().Be("application/octet-stream");
    }

    [Theory]
    [InlineData("image.png", "image/png")]
    [InlineData("image.jpeg", "image/jpeg")]
    [InlineData("image.jpg", "image/jpeg")]
    [InlineData("image.bmp", "image/bmp")]
    [InlineData("image.gif", "image/gif")]
    [InlineData("image.tiff", "image/tiff")]
    [InlineData("image.tif", "image/tiff")]
    [InlineData("image.svg", "image/svg+xml")]
    public void try_get_mime_type_should_return_true_and_correct_mime_type_for_known_image_extensions(
        string fileName,
        string expectedMimeType
    )
    {
        // when
        var result = _mimeTypeProvider.TryGetMimeType(fileName, out var mimeType);

        // then
        result.Should().BeTrue();
        mimeType.Should().Be(expectedMimeType);
    }

    [Fact]
    public void try_get_mime_type_should_return_false_and_null_for_unknown_file_extension()
    {
        // given
        const string fileName = "unknownfile.unknown";

        // when
        var result = _mimeTypeProvider.TryGetMimeType(fileName, out var mimeType);

        // then
        result.Should().BeFalse();
        mimeType.Should().BeNull();
    }

    [Theory]
    [InlineData("text/html", new[] { "html", "htm" })]
    [InlineData("text/plain", new[] { "txt", "log", "ini", "in", "list" })]
    [InlineData("application/json", new[] { "json", "map" })]
    [InlineData("application/xml", new[] { "xml", "rng", "xsd", "xsl" })]
    [InlineData("image/jpeg", new[] { "jpeg", "jpg", "jpe" })]
    [InlineData("image/png", new[] { "png" })]
    [InlineData("application/pdf", new[] { "pdf" })]
    [InlineData("text/css", new[] { "css" })]
    [InlineData("application/javascript", new[] { "js" })]
    public void get_mime_type_extensions_should_return_correct_extensions_for_known_mime_types(
        string mimeType,
        string[] expectedExtensions
    )
    {
        // when
        var extensions = _mimeTypeProvider.GetMimeTypeExtensions(mimeType);

        // then
        extensions.Should().Contain(expectedExtensions);
    }
}
