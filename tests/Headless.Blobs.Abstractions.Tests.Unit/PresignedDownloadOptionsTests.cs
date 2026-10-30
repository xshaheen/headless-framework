// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Blobs;
using Headless.Testing.Tests;

namespace Tests;

public sealed class PresignedDownloadOptionsTests : TestBase
{
    [Theory]
    [InlineData("report.pdf")]
    [InlineData("تقرير مارس.pdf")]
    [InlineData("a \"quoted\" name.txt")]
    public void should_accept_a_file_name_when_it_has_no_control_characters(string fileName)
    {
        // when
        var options = new PresignedDownloadOptions { FileName = fileName };

        // then
        options.FileName.Should().Be(fileName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("report.pdf\r\nSet-Cookie: a=b")]
    [InlineData("report\n.pdf")]
    [InlineData("report\0.pdf")]
    [InlineData("report\u007F.pdf")]
    public void should_reject_a_file_name_when_it_is_blank_or_has_control_characters(string fileName)
    {
        // when
        var act = () => new PresignedDownloadOptions { FileName = fileName };

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("text/plain; charset=utf-8")]
    public void should_accept_a_content_type_when_it_is_a_single_media_type(string contentType)
    {
        // when
        var options = new PresignedDownloadOptions { ContentType = contentType };

        // then
        options.ContentType.Should().Be(contentType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a media type")]
    [InlineData("image/*")]
    [InlineData("*/*")]
    [InlineData("text/plain\r\nX-Injected: 1")]
    [InlineData("text/plain\n")]
    public void should_reject_a_content_type_when_it_is_not_a_single_media_type(string contentType)
    {
        // when
        var act = () => new PresignedDownloadOptions { ContentType = contentType };

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_accept_null_for_both_members()
    {
        // when
        var options = new PresignedDownloadOptions { FileName = null, ContentType = null };

        // then
        options.FileName.Should().BeNull();
        options.ContentType.Should().BeNull();
    }
}
