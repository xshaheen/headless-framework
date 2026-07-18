// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;
using Headless.Testing.Tests;
using MimeKit;

namespace Tests;

public sealed class EmailToMimeMessageConverterTests : TestBase
{
    [Fact]
    public async Task should_map_headers_and_both_bodies()
    {
        var request = new SendSingleEmailRequest
        {
            From = new EmailRequestAddress("from@example.com", "Sender"),
            Destination = new EmailRequestDestination
            {
                ToAddresses = [new EmailRequestAddress("to@example.com")],
                CcAddresses = [new EmailRequestAddress("cc@example.com")],
                BccAddresses = [new EmailRequestAddress("bcc@example.com")],
            },
            Subject = "Hello",
            MessageText = "plain",
            MessageHtml = "<p>rich</p>",
        };

        using var message = await request.ConvertToMimeMessageAsync(AbortToken);

        message.Subject.Should().Be("Hello");
        message.From.Mailboxes.Single().Address.Should().Be("from@example.com");
        message.To.Mailboxes.Single().Address.Should().Be("to@example.com");
        message.Cc.Mailboxes.Single().Address.Should().Be("cc@example.com");
        message.Bcc.Mailboxes.Single().Address.Should().Be("bcc@example.com");
        message.TextBody.Should().Be("plain");
        message.HtmlBody.Should().Be("<p>rich</p>");
    }

    [Fact]
    public void should_fall_back_to_address_when_display_name()
    {
        var address = new EmailRequestAddress("user@example.com");

        var mailbox = address.MapToMailboxAddress();

        mailbox.Name.Should().Be("user@example.com");
        mailbox.Address.Should().Be("user@example.com");
    }

    [Fact]
    public async Task should_attach_file_with_explicit_content_type()
    {
        var request = new SendSingleEmailRequest
        {
            From = "from@example.com",
            Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress("to@example.com")] },
            Subject = "with attachment",
            MessageText = "see attached",
            Attachments =
            [
                new EmailRequestAttachment
                {
                    Name = "invoice.pdf",
                    File = new byte[] { 1, 2, 3, 4 },
                    ContentType = "application/pdf",
                },
            ],
        };

        using var message = await request.ConvertToMimeMessageAsync(AbortToken);

        var attachment = message.Attachments.OfType<MimePart>().Single();
        attachment.FileName.Should().Be("invoice.pdf");
        attachment.ContentType.MimeType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task should_rethrow_when_cancelled_during_attachment_build()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var request = new SendSingleEmailRequest
        {
            From = "from@example.com",
            Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress("to@example.com")] },
            Subject = "cancelled",
            MessageText = "body",
            Attachments = [new EmailRequestAttachment { Name = "a.bin", File = new byte[] { 1, 2, 3 } }],
        };

        var act = async () => await request.ConvertToMimeMessageAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
