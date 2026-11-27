// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Emails;
using Headless.Testing.Tests;
using MimeKit;

namespace Tests;

/// <summary>An email's HTML preview, its <c>.eml</c>, and its attachment downloads over the real capture store.</summary>
public sealed class EmailContentEndpointTests : TestBase
{
    private static readonly byte[] _Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    [Fact]
    public async Task should_serve_the_html_under_the_sandboxed_preview_policy_with_inline_images()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);
        var email = await DevInboxTestHost.SendEmailAsync(
            app,
            "Welcome",
            AbortToken,
            html: """<p style="color:red">Hi</p><img src="cid:logo.png">""",
            attachments:
            [
                new()
                {
                    Name = "logo.png",
                    File = _Png,
                    ContentType = "image/png",
                },
            ]
        );

        // when
        using var response = await DevInboxTestHost.GetAsync(
            client,
            $"{DevInboxTestHost.Api}/emails/{email.Id}/html",
            AbortToken
        );
        var html = await response.Content.ReadAsStringAsync(AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response
            .Headers.GetValues("Content-Security-Policy")
            .Single()
            .Should()
            .StartWith("sandbox; default-src 'none'");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        html.Should().Contain("data:image/png;base64," + Convert.ToBase64String(_Png)).And.NotContain("cid:");
    }

    [Fact]
    public async Task should_answer_not_found_for_the_html_of_an_email_without_an_html_body()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);
        var email = await DevInboxTestHost.SendEmailAsync(app, "Plain", AbortToken, text: "Only text.");

        // when
        using var response = await DevInboxTestHost.GetAsync(
            client,
            $"{DevInboxTestHost.Api}/emails/{email.Id}/html",
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task should_download_the_eml_as_an_attachment_that_parses_back_to_the_same_message()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);
        var email = await DevInboxTestHost.SendEmailAsync(app, "Your receipt", AbortToken);

        // when
        using var response = await DevInboxTestHost.GetAsync(
            client,
            $"{DevInboxTestHost.Api}/emails/{email.Id}/eml",
            AbortToken
        );
        await using var body = await response.Content.ReadAsStreamAsync(AbortToken);
        var message = await MimeMessage.LoadAsync(body, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("message/rfc822");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileName.Should().Be($"{email.Id:D}.eml");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        message.Subject.Should().Be("Your receipt");
    }

    [Fact]
    public async Task should_download_a_kept_attachment_with_its_bytes_as_an_unsniffed_attachment()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);
        var email = await DevInboxTestHost.SendEmailAsync(
            app,
            "Invoice",
            AbortToken,
            attachments: [new() { Name = "invoice.pdf", File = _Png }]
        );

        // when
        using var response = await DevInboxTestHost.GetAsync(
            client,
            $"{DevInboxTestHost.Api}/emails/{email.Id}/attachments/0",
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync(AbortToken)).Should().Equal(_Png);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Headers.GetValues("X-Content-Type-Options").Single().Should().Be("nosniff");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Theory]
    [InlineData("page.html", "text/html")]
    [InlineData("icon.svg", "image/svg+xml")]
    public async Task should_download_an_active_content_type_as_opaque_bytes(string name, string contentType)
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);
        var email = await DevInboxTestHost.SendEmailAsync(
            app,
            "Active",
            AbortToken,
            attachments:
            [
                new()
                {
                    Name = name,
                    File = "<script>alert(1)</script>"u8.ToArray(),
                    ContentType = contentType,
                },
            ]
        );

        // when
        using var response = await DevInboxTestHost.GetAsync(
            client,
            $"{DevInboxTestHost.Api}/emails/{email.Id}/attachments/0",
            AbortToken
        );

        // then
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task should_keep_a_line_break_in_a_file_name_from_adding_a_header()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);
        var email = await DevInboxTestHost.SendEmailAsync(
            app,
            "Injected",
            AbortToken,
            attachments: [new() { Name = "a\r\nSet-Cookie: x.txt", File = _Png }]
        );

        // when
        using var response = await DevInboxTestHost.GetAsync(
            client,
            $"{DevInboxTestHost.Api}/emails/{email.Id}/attachments/0",
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.GetValues("Content-Disposition").Should().ContainSingle();
        response.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("5")]
    [InlineData("-1")]
    public async Task should_answer_not_found_for_an_omitted_or_missing_attachment(string index)
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);
        var email = await DevInboxTestHost.SendEmailAsync(
            app,
            "Large",
            AbortToken,
            attachments: [new() { Name = "big.bin", File = new byte[DevInboxTestHost.MaxCapturedAttachmentBytes + 1] }]
        );

        // when
        using var response = await DevInboxTestHost.GetAsync(
            client,
            $"{DevInboxTestHost.Api}/emails/{email.Id}/attachments/{index}",
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
