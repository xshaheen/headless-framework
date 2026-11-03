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
    public async Task should_attach_only_the_sliced_bytes_when_memory_has_an_offset()
    {
        // The converter wraps the attachment's backing array instead of copying it, so a ReadOnlyMemory that is a
        // slice of a larger array must still contribute exactly its own window — never the surrounding bytes.
        var backing = new byte[] { 0xAA, 0xBB, 1, 2, 3, 4, 0xCC };

        var request = new SendSingleEmailRequest
        {
            From = "from@example.com",
            Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress("to@example.com")] },
            Subject = "sliced attachment",
            MessageText = "see attached",
            Attachments = [new EmailRequestAttachment { Name = "slice.bin", File = backing.AsMemory(2, 4) }],
        };

        using var message = await request.ConvertToMimeMessageAsync(AbortToken);

        var attachment = message.Attachments.OfType<MimePart>().Single();
        await using var decoded = new MemoryStream();
        await attachment.Content!.DecodeToAsync(decoded, AbortToken);

        decoded.ToArray().Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task should_encode_a_binary_attachment_from_the_callers_bytes_without_copying_them()
    {
        var file = new byte[] { 1, 2, 3, 4 };

        var request = new SendSingleEmailRequest
        {
            From = "from@example.com",
            Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress("to@example.com")] },
            Subject = "binary attachment",
            MessageText = "see attached",
            Attachments = [new EmailRequestAttachment { Name = "photo.png", File = file }],
        };

        using var message = await request.ConvertToMimeMessageAsync(AbortToken);

        var attachment = message.Attachments.OfType<MimePart>().Single();
        attachment.ContentType.MimeType.Should().Be("image/png");
        attachment.FileName.Should().Be("photo.png");
        attachment.IsAttachment.Should().BeTrue();
        attachment.ContentTransferEncoding.Should().Be(ContentEncoding.Base64);
        var content = attachment.Content!.Stream.Should().BeOfType<MemoryStream>().Subject;
        content.TryGetBuffer(out var buffer).Should().BeTrue();
        buffer.Array.Should().BeSameAs(file);
    }

    [Fact]
    public async Task should_write_a_binary_attachment_on_every_write_of_the_message()
    {
        var request = new SendSingleEmailRequest
        {
            From = "from@example.com",
            Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress("to@example.com")] },
            Subject = "written twice",
            MessageText = "see attached",
            Attachments = [new EmailRequestAttachment { Name = "a.bin", File = "hello"u8.ToArray() }],
        };

        using var message = await request.ConvertToMimeMessageAsync(AbortToken);

        // A retried send writes the message again, so the part must re-read its content from the start.
        await using var first = new MemoryStream();
        await message.WriteToAsync(first, AbortToken);
        await using var second = new MemoryStream();
        await message.WriteToAsync(second, AbortToken);

        var expected = Convert.ToBase64String("hello"u8.ToArray());
        Encoding.ASCII.GetString(first.ToArray()).Should().Contain(expected);
        second.ToArray().Should().Equal(first.ToArray());
    }

    [Fact]
    public async Task should_keep_mimekit_choosing_the_encoding_of_a_text_attachment()
    {
        var request = new SendSingleEmailRequest
        {
            From = "from@example.com",
            Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress("to@example.com")] },
            Subject = "text attachment",
            MessageText = "see attached",
            Attachments = [new EmailRequestAttachment { Name = "notes.txt", File = "plain ascii notes"u8.ToArray() }],
        };

        using var message = await request.ConvertToMimeMessageAsync(AbortToken);

        var attachment = message.Attachments.OfType<MimePart>().Single();
        attachment.ContentType.MimeType.Should().Be("text/plain");
        attachment.ContentTransferEncoding.Should().NotBe(ContentEncoding.Base64);
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
