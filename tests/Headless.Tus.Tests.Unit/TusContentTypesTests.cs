// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Tus;

namespace Tests;

public sealed class TusContentTypesTests : TestBase
{
    [Theory]
    [InlineData("image/png", "image/png")]
    [InlineData("application/pdf", "application/pdf")]
    [InlineData("text/plain; charset=utf-8", "text/plain; charset=utf-8")]
    [InlineData("  video/mp4 ", "video/mp4")]
    [InlineData("application/json", "application/json")]
    public void should_keep_a_well_formed_filetype(string fileType, string expected)
    {
        TusContentTypes.FromFileType(_Metadata(fileType)).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("png")]
    [InlineData("image/")]
    [InlineData("image/png, text/html")]
    [InlineData("image/png\r\nx-amz-acl: public-read")]
    public void should_fall_back_to_octet_stream_when_the_filetype_is_malformed(string fileType)
    {
        TusContentTypes.FromFileType(_Metadata(fileType)).Should().Be("application/octet-stream");
    }

    [Fact]
    public void should_fall_back_to_octet_stream_without_a_filetype()
    {
        // tus clients send the key in lower case, and keys are matched as sent
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FileType"] = "image/png",
            ["filename"] = "a.png",
        };

        TusContentTypes.FromFileType(metadata).Should().Be("application/octet-stream");
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("application/xhtml+xml")]
    [InlineData("image/svg+xml")]
    [InlineData("text/xml")]
    [InlineData("application/xml")]
    [InlineData("text/javascript")]
    [InlineData("application/javascript")]
    [InlineData("application/ecmascript")]
    [InlineData("text/ecmascript")]
    [InlineData("application/x-javascript")]
    [InlineData("text/x-javascript")]
    [InlineData("text/jscript")]
    [InlineData("text/xsl")]
    [InlineData("application/rss+xml")]
    [InlineData("application/vnd.custom+XML")]
    public void should_fall_back_to_octet_stream_for_a_script_capable_filetype(string fileType)
    {
        TusContentTypes.FromFileType(_Metadata(fileType)).Should().Be("application/octet-stream");
    }

    [Theory]
    [InlineData("TEXT/HTML")]
    [InlineData("Image/Svg+Xml")]
    [InlineData("text/html; charset=utf-8")]
    [InlineData(" text/html ;charset=utf-8")]
    [InlineData("application/xhtml+xml; q=1")]
    public void should_ignore_case_and_parameters_when_matching_a_script_capable_filetype(string fileType)
    {
        TusContentTypes.FromFileType(_Metadata(fileType)).Should().Be("application/octet-stream");
    }

    [Fact]
    public void should_not_treat_a_type_that_only_contains_xml_as_script_capable()
    {
        // application/xml-dtd neither matches a blocked type nor ends in "+xml"
        TusContentTypes.FromFileType(_Metadata("application/xml-dtd")).Should().Be("application/xml-dtd");
    }

    private static Dictionary<string, string> _Metadata(string fileType)
    {
        return new(StringComparer.Ordinal) { [TusContentTypes.FileTypeMetadataKey] = fileType };
    }
}
