// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DevInbox.Dashboard.Internal;
using Headless.Emails;
using Headless.Testing.Tests;

namespace Tests;

public sealed class CidImageInlinerTests : TestBase
{
    private static readonly byte[] _Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    private static readonly string _PngUri = "data:image/png;base64," + Convert.ToBase64String(_Png);

    [Fact]
    public void should_inline_a_kept_png_attachment_referenced_by_its_file_name()
    {
        var html = CidImageInliner.Inline("""<img src="cid:logo.png">""", [_Kept("logo.png", "image/png")]);

        html.Should().Be($"""<img src="{_PngUri}">""");
    }

    [Fact]
    public void should_match_the_name_before_an_at_sign_ignoring_case()
    {
        var html = CidImageInliner.Inline("<img src='cid:LOGO.png@example.com'>", [_Kept("logo.png", "image/png")]);

        html.Should().Be($"<img src='{_PngUri}'>");
    }

    [Fact]
    public void should_resolve_the_type_of_an_attachment_captured_without_one_from_its_file_name()
    {
        var png = CidImageInliner.Inline("""<img src="cid:logo.png">""", [_Kept("logo.png", contentType: null)]);
        var svg = CidImageInliner.Inline("""<img src="cid:logo.svg">""", [_Kept("logo.svg", contentType: null)]);

        png.Should().Be($"""<img src="{_PngUri}">""");
        svg.Should().Be("""<img src="cid:logo.svg">""");
    }

    [Fact]
    public void should_rewrite_a_css_url_inside_a_style_attribute()
    {
        var html = CidImageInliner.Inline(
            """<td style="background: url(cid:hero.jpg)">""",
            [_Kept("hero.jpg", "image/jpeg")]
        );

        html.Should().Be($"""<td style="background: url(data:image/jpeg;base64,{Convert.ToBase64String(_Png)})">""");
    }

    [Fact]
    public void should_leave_unmatched_omitted_and_svg_references_unchanged()
    {
        const string html = """<img src="cid:missing.png"><img src="cid:big.png"><img src="cid:icon.svg">""";
        CapturedEmailAttachment[] attachments =
        [
            new()
            {
                FileName = "big.png",
                ContentType = "image/png",
                SizeBytes = 10_000,
            },
            _Kept("icon.svg", "image/svg+xml"),
        ];

        CidImageInliner.Inline(html, attachments).Should().Be(html);
    }

    [Fact]
    public void should_return_html_without_a_cid_reference_unchanged()
    {
        const string html = "<p>Hello <img src=\"https://example.com/x.png\"></p>";

        CidImageInliner.Inline(html, [_Kept("logo.png", "image/png")]).Should().BeSameAs(html);
    }

    [Fact]
    public void should_keep_repeated_references_within_the_inline_budget()
    {
        // given: an image whose data: URI is about 100 KB, referenced far more often than the budget can hold
        var image = new byte[75_000];
        var uriLength = "data:image/png;base64,".Length + Convert.ToBase64String(image).Length;
        var fits = CidImageInliner.MaxInlineLength / uriLength;
        var html = string.Concat(Enumerable.Repeat("""<img src="cid:logo.png">""", fits + 10));
        CapturedEmailAttachment[] attachments =
        [
            new()
            {
                FileName = "logo.png",
                ContentType = "image/png",
                SizeBytes = image.Length,
                Content = image,
            },
        ];

        // when
        var inlined = CidImageInliner.Inline(html, attachments);

        // then
        _Occurrences(inlined, "data:image/png").Should().Be(fits);
        _Occurrences(inlined, "cid:logo.png").Should().Be(10);
        (inlined.Length - html.Length).Should().BeLessThanOrEqualTo(CidImageInliner.MaxInlineLength);
    }

    [Fact]
    public void should_skip_an_image_larger_than_the_budget_and_still_inline_a_later_small_one()
    {
        // given: one image whose data: URI alone is past the budget, then a small one
        var huge = new byte[CidImageInliner.MaxInlineLength];
        const string html = """<img src="cid:huge.png"><img src="cid:logo.png">""";
        CapturedEmailAttachment[] attachments =
        [
            new()
            {
                FileName = "huge.png",
                ContentType = "image/png",
                SizeBytes = huge.Length,
                Content = huge,
            },
            _Kept("logo.png", "image/png"),
        ];

        // when
        var inlined = CidImageInliner.Inline(html, attachments);

        // then
        inlined.Should().Be($"""<img src="cid:huge.png"><img src="{_PngUri}">""");
    }

    private static int _Occurrences(string text, string value)
    {
        var count = 0;

        for (
            var at = text.IndexOf(value, StringComparison.Ordinal);
            at >= 0;
            at = text.IndexOf(value, at + 1, StringComparison.Ordinal)
        )
        {
            count++;
        }

        return count;
    }

    private static CapturedEmailAttachment _Kept(string fileName, string? contentType) =>
        new()
        {
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = _Png.Length,
            Content = _Png,
        };
}
