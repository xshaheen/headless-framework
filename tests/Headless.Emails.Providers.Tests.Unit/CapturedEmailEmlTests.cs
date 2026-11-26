// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;
using Headless.Testing.Tests;
using MimeKit;

namespace Tests;

public sealed class CapturedEmailEmlTests : TestBase
{
    private static readonly Guid _Id = Guid.Parse("0193f1a2-0000-7000-8000-000000000001");

    [Fact]
    public async Task should_round_trip_headers_and_both_bodies_through_a_mime_parser()
    {
        // given
        var email = _Email() with
        {
            From = new EmailRequestAddress("orders@example.com", "Orders \"Team\""),
            To = [new EmailRequestAddress("lina@example.com", "Lina"), new EmailRequestAddress("omar@example.com")],
            Cc = [new EmailRequestAddress("cc@example.com")],
            Bcc = [new EmailRequestAddress("bcc@example.com")],
            HtmlBody = "<p>Hello</p>",
            TextBody = "Hello",
        };

        // when
        var message = await _ParseAsync(await _EmlAsync(email));

        // then
        message.Subject.Should().Be("Your order");
        message.MessageId.Should().Be($"{_Id:N}@dev-inbox.headless");
        message.Date.Should().Be(email.CapturedAt);
        message
            .From.Mailboxes.Single()
            .Should()
            .BeEquivalentTo(new { Name = "Orders \"Team\"", Address = "orders@example.com" });
        message.To.Mailboxes.Select(m => m.Address).Should().Equal("lina@example.com", "omar@example.com");
        message.To.Mailboxes.First().Name.Should().Be("Lina");
        message.Cc.Mailboxes.Single().Address.Should().Be("cc@example.com");
        message.Bcc.Mailboxes.Single().Address.Should().Be("bcc@example.com");
        message.Body!.ContentType.MimeType.Should().Be("multipart/alternative");
        message.TextBody!.TrimEnd().Should().Be("Hello");
        message.HtmlBody!.TrimEnd().Should().Be("<p>Hello</p>");
        message.Headers["X-Headless-Sender"].Should().Be("audit");
    }

    [Fact]
    public async Task should_encode_non_ascii_subject_display_name_and_body()
    {
        // given
        var subject = "طلبك جاهز — " + new string('é', 60) + " 🎉";
        var email = _Email() with
        {
            Subject = subject,
            From = new EmailRequestAddress("orders@example.com", "متجر الطلبات"),
            TextBody = "مرحبا يا لينا",
        };

        // when
        var eml = await _EmlAsync(email);
        var message = await _ParseAsync(eml);

        // then
        eml.Should().NotContain("طلبك");
        message.Subject.Should().Be(subject);
        message.From.Mailboxes.Single().Name.Should().Be("متجر الطلبات");
        message.TextBody!.TrimEnd().Should().Be("مرحبا يا لينا");
    }

    [Fact]
    public async Task should_use_crlf_line_endings_and_keep_lines_under_the_limit()
    {
        var email = _Email() with { TextBody = new string('x', 5000) };

        var eml = await _EmlAsync(email);

        eml.Replace("\r\n", "", StringComparison.Ordinal).Should().NotContain("\n");
        eml.Split("\r\n").Should().OnlyContain(line => line.Length <= 998);
    }

    [Fact]
    public async Task should_write_a_single_html_part_when_there_is_no_text_body()
    {
        var message = await _ParseAsync(await _EmlAsync(_Email() with { TextBody = null, HtmlBody = "<b>hi</b>" }));

        message.Body!.ContentType.MimeType.Should().Be("text/html");
        message.HtmlBody!.TrimEnd().Should().Be("<b>hi</b>");
    }

    [Fact]
    public async Task should_write_an_empty_text_part_for_a_failed_capture_without_a_body()
    {
        var message = await _ParseAsync(
            await _EmlAsync(_Email() with { TextBody = null, FailureError = "At least one body must be provided." })
        );

        message.Body!.ContentType.MimeType.Should().Be("text/plain");
        message.Headers["X-Headless-Failure"].Should().Be("At least one body must be provided.");
    }

    [Fact]
    public async Task should_attach_kept_content_and_list_omitted_attachments_in_a_header()
    {
        // given
        var email = _Email() with
        {
            Attachments =
            [
                new CapturedEmailAttachment
                {
                    FileName = "فاتورة.txt",
                    ContentType = "text/plain",
                    SizeBytes = 5,
                    Content = "hello"u8.ToArray(),
                },
                new CapturedEmailAttachment
                {
                    FileName = "big.pdf",
                    ContentType = "application/pdf",
                    SizeBytes = 9000,
                },
            ],
            TraceId = "4bf92f3577b34da6a3ce929d0e0e4736",
            CorrelationId = "corr-1",
        };

        // when
        var message = await _ParseAsync(await _EmlAsync(email));

        // then
        message.Body!.ContentType.MimeType.Should().Be("multipart/mixed");
        var attachment = message.Attachments.OfType<MimePart>().Should().ContainSingle().Subject;
        attachment.FileName.Should().Be("فاتورة.txt");

        await using var content = new MemoryStream();
        await attachment.Content!.DecodeToAsync(content, AbortToken);
        content.ToArray().Should().Equal("hello"u8.ToArray());

        message.Headers["X-Headless-Attachment-Omitted"].Should().Be("big.pdf; size=9000; type=application/pdf");
        message.Headers["X-Headless-Trace-Id"].Should().Be("4bf92f3577b34da6a3ce929d0e0e4736");
        message.Headers["X-Headless-Correlation-Id"].Should().Be("corr-1");
        message.TextBody!.TrimEnd().Should().Be("Hello");
    }

    [Fact]
    public async Task should_keep_line_breaks_in_caller_values_from_injecting_headers()
    {
        // given
        var email = _Email() with
        {
            From = new EmailRequestAddress("from@example.com\r\nX-Injected: 1", "Name\r\nX-Injected: 2"),
            Subject = "Hi\r\nX-Injected: 3",
            Attachments =
            [
                new CapturedEmailAttachment
                {
                    FileName = "a\r\nX-Injected: 4.txt",
                    ContentType = "text/plain\r\nX-Injected: 5",
                    SizeBytes = 1,
                    Content = "x"u8.ToArray(),
                },
            ],
        };

        // when
        var eml = await _EmlAsync(email);
        var message = await _ParseAsync(eml);

        // then
        message.Headers.Contains("X-Injected").Should().BeFalse();
        message.Subject.Should().StartWith("Hi").And.EndWith("X-Injected: 3");
        var from = message.From.Mailboxes.Single();
        from.Address.Should().Be("invalid-address@dev-inbox.invalid");
        from.Name.Should().StartWith("Name").And.Contain("X-Injected: 1");
        message.Attachments.OfType<MimePart>().Single().ContentType.MimeType.Should().Be("text/plain");
    }

    [Fact]
    public async Task should_fold_a_subject_longer_than_the_line_limit()
    {
        var subject = string.Join(' ', Enumerable.Repeat("word", 300));

        var eml = await _EmlAsync(_Email() with { Subject = subject });
        var message = await _ParseAsync(eml);

        eml.Split("\r\n").Should().OnlyContain(line => line.Length <= 998);
        message.Subject.Should().Be(subject);
    }

    [Fact]
    public async Task should_write_the_capture_time_with_its_offset_as_the_date()
    {
        var email = _Email() with { CapturedAt = new DateTimeOffset(2026, 3, 1, 9, 5, 7, TimeSpan.FromHours(-5.5)) };

        var eml = await _EmlAsync(email);

        eml.Should().Contain("Date: Sun, 01 Mar 2026 09:05:07 -0530\r\n");
    }

    private static async Task<string> _EmlAsync(CapturedEmail email)
    {
        await using var stream = new MemoryStream();
        await email.WriteEmlAsync(stream, AbortToken);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private async Task<MimeMessage> _ParseAsync(string eml)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(eml));

        return await MimeMessage.LoadAsync(stream, AbortToken);
    }

    private static CapturedEmail _Email()
    {
        return new CapturedEmail
        {
            Id = _Id,
            Sequence = 1,
            CapturedAt = new DateTimeOffset(2026, 11, 4, 14, 20, 31, TimeSpan.Zero),
            SenderName = "audit",
            From = "orders@example.com",
            To = [new EmailRequestAddress("lina@example.com")],
            Subject = "Your order",
            TextBody = "Hello",
        };
    }
}
