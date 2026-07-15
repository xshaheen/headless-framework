// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;
using Headless.Emails.Dev;
using Headless.Testing.Tests;

namespace Tests;

public sealed class DevEmailSenderTests : TestBase
{
    [Fact]
    public async Task should_append_the_message_to_the_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"emails-dev-{Guid.NewGuid():N}.txt");
        using var sender = new DevEmailSender(path);

        try
        {
            var result = await sender.SendAsync(_Request(), AbortToken);

            result.Success.Should().BeTrue();
            var contents = await File.ReadAllTextAsync(path, AbortToken);
            contents.Should().Contain("from@example.com").And.Contain("to@example.com").And.Contain("hello dev");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task should_serialize_concurrent_writes_without_corruption()
    {
        var path = Path.Combine(Path.GetTempPath(), $"emails-dev-{Guid.NewGuid():N}.txt");
        using var sender = new DevEmailSender(path);

        try
        {
            var sends = Enumerable
                .Range(0, 20)
                .Select(i => sender.SendAsync(_Request(text: $"body-{i}"), AbortToken).AsTask());
            await Task.WhenAll(sends);

            var contents = await File.ReadAllTextAsync(path, AbortToken);
            // 20 sends -> 20 separators, and every message body present exactly once (no interleaving).
            (contents.Split("--------------------").Length - 1)
                .Should()
                .Be(20);
            foreach (var i in Enumerable.Range(0, 20))
            {
                contents.Should().Contain($"body-{i}");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task should_throw_when_missing_body()
    {
        var path = Path.Combine(Path.GetTempPath(), $"emails-dev-{Guid.NewGuid():N}.txt");
        using var sender = new DevEmailSender(path);

        var request = _Request() with { MessageText = null, MessageHtml = null };

        var act = async () => await sender.SendAsync(request, AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void should_reject_empty_path_when_constructor()
    {
        var act = () => new DevEmailSender("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task should_render_cc_and_bcc_lines_when_present()
    {
        var path = Path.Combine(Path.GetTempPath(), $"emails-dev-{Guid.NewGuid():N}.txt");
        using var sender = new DevEmailSender(path);

        try
        {
            var request = _Request(
                cc: [new EmailRequestAddress("cc@example.com")],
                bcc: [new EmailRequestAddress("bcc@example.com")]
            );

            await sender.SendAsync(request, AbortToken);

            var contents = await File.ReadAllTextAsync(path, AbortToken);
            contents.Should().Contain("Cc: cc@example.com").And.Contain("Bcc: bcc@example.com");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task should_omit_cc_and_bcc_lines_when_absent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"emails-dev-{Guid.NewGuid():N}.txt");
        using var sender = new DevEmailSender(path);

        try
        {
            await sender.SendAsync(_Request(), AbortToken);

            var contents = await File.ReadAllTextAsync(path, AbortToken);
            contents.Should().NotContain("Cc:").And.NotContain("Bcc:");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task should_render_attachment_names_without_content()
    {
        var path = Path.Combine(Path.GetTempPath(), $"emails-dev-{Guid.NewGuid():N}.txt");
        using var sender = new DevEmailSender(path);

        try
        {
            var request = _Request(
                attachments:
                [
                    new EmailRequestAttachment
                    {
                        Name = "invoice.pdf",
                        File = "RAW-ATTACHMENT-BYTES"u8.ToArray(),
                        ContentType = "application/pdf",
                    },
                ]
            );

            await sender.SendAsync(request, AbortToken);

            // Only the attachment name is recorded — raw bytes would bloat the dev file.
            var contents = await File.ReadAllTextAsync(path, AbortToken);
            contents.Should().Contain("Attachments:").And.Contain("  Name: invoice.pdf");
            contents.Should().NotContain("RAW-ATTACHMENT-BYTES");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task should_write_html_body_when_text_is_missing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"emails-dev-{Guid.NewGuid():N}.txt");
        using var sender = new DevEmailSender(path);

        try
        {
            await sender.SendAsync(_Request(text: null, html: "<p>hello html</p>"), AbortToken);

            var contents = await File.ReadAllTextAsync(path, AbortToken);
            contents.Should().Contain("<p>hello html</p>");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task should_prefer_text_body_when_both_bodies_present()
    {
        var path = Path.Combine(Path.GetTempPath(), $"emails-dev-{Guid.NewGuid():N}.txt");
        using var sender = new DevEmailSender(path);

        try
        {
            await sender.SendAsync(_Request(text: "plain body", html: "<p>html body</p>"), AbortToken);

            var contents = await File.ReadAllTextAsync(path, AbortToken);
            contents.Should().Contain("plain body").And.NotContain("<p>html body</p>");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task should_normalize_newlines_in_text_body()
    {
        var path = Path.Combine(Path.GetTempPath(), $"emails-dev-{Guid.NewGuid():N}.txt");
        using var sender = new DevEmailSender(path);

        try
        {
            await sender.SendAsync(_Request(text: "line1\r\nline2\nline3"), AbortToken);

            // Mixed CRLF/LF input is normalized to the platform newline before being appended.
            var contents = await File.ReadAllTextAsync(path, AbortToken);
            contents.Should().Contain($"line1{Environment.NewLine}line2{Environment.NewLine}line3");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task should_render_display_name_in_from_header()
    {
        var path = Path.Combine(Path.GetTempPath(), $"emails-dev-{Guid.NewGuid():N}.txt");
        using var sender = new DevEmailSender(path);

        try
        {
            var request = _Request() with { From = new EmailRequestAddress("from@example.com", "Alice") };

            await sender.SendAsync(request, AbortToken);

            var contents = await File.ReadAllTextAsync(path, AbortToken);
            contents.Should().Contain("From: Alice <from@example.com>");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task should_honor_already_cancelled_token()
    {
        var path = Path.Combine(Path.GetTempPath(), $"emails-dev-{Guid.NewGuid():N}.txt");
        using var sender = new DevEmailSender(path);

        try
        {
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            var act = async () => await sender.SendAsync(_Request(), cts.Token);

            // Cancellation must surface before any file I/O happens.
            await act.Should().ThrowAsync<OperationCanceledException>();
            File.Exists(path).Should().BeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static SendSingleEmailRequest _Request(
        string? text = "hello dev",
        string? html = null,
        IReadOnlyList<EmailRequestAddress>? cc = null,
        IReadOnlyList<EmailRequestAddress>? bcc = null,
        IReadOnlyList<EmailRequestAttachment>? attachments = null
    ) =>
        new()
        {
            From = "from@example.com",
            Destination = new EmailRequestDestination
            {
                ToAddresses = [new EmailRequestAddress("to@example.com")],
                CcAddresses = cc ?? [],
                BccAddresses = bcc ?? [],
            },
            Subject = "subject",
            MessageText = text,
            MessageHtml = html,
            Attachments = attachments ?? [],
        };
}

public sealed class NoopEmailSenderTests : TestBase
{
    [Fact]
    public async Task should_report_success()
    {
        var request = new SendSingleEmailRequest
        {
            From = "from@example.com",
            Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress("to@example.com")] },
            Subject = "subject",
            MessageText = "body",
        };

        var result = await new NoopEmailSender().SendAsync(request, AbortToken);

        result.Success.Should().BeTrue();
    }
}
