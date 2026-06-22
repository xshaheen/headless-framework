// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Emails;
using Headless.Emails.Azure;

namespace Tests;

public sealed class AzureCommunicationEmailMessageMapperTests
{
    [Fact]
    public void should_map_sender_subject_and_single_recipient()
    {
        // given
        var request = new SendSingleEmailRequest
        {
            From = new EmailRequestAddress("sender@example.com", "Sender Name"),
            Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress("to@example.com")] },
            Subject = "Hello",
            MessageHtml = "<p>Hi</p>",
        };

        // when
        var message = AzureCommunicationEmailMessageMapper.ToEmailMessage(request);

        // then
        message.SenderAddress.Should().Be("sender@example.com");
        message.Content.Subject.Should().Be("Hello");
        message.Recipients.To.Should().ContainSingle();
        message.Recipients.To[0].Address.Should().Be("to@example.com");
    }

    [Fact]
    public void should_map_to_cc_and_bcc_preserving_display_names()
    {
        // given
        var request = new SendSingleEmailRequest
        {
            From = new EmailRequestAddress("sender@example.com"),
            Destination = new EmailRequestDestination
            {
                ToAddresses = [new EmailRequestAddress("to@example.com", "To Person")],
                CcAddresses = [new EmailRequestAddress("cc@example.com", "Cc Person")],
                BccAddresses = [new EmailRequestAddress("bcc@example.com", "Bcc Person")],
            },
            Subject = "Subject",
            MessageText = "body",
        };

        // when
        var message = AzureCommunicationEmailMessageMapper.ToEmailMessage(request);

        // then
        message.Recipients.To.Should().ContainSingle();
        message.Recipients.To[0].Address.Should().Be("to@example.com");
        message.Recipients.To[0].DisplayName.Should().Be("To Person");

        message.Recipients.CC.Should().ContainSingle();
        message.Recipients.CC[0].Address.Should().Be("cc@example.com");
        message.Recipients.CC[0].DisplayName.Should().Be("Cc Person");

        message.Recipients.BCC.Should().ContainSingle();
        message.Recipients.BCC[0].Address.Should().Be("bcc@example.com");
        message.Recipients.BCC[0].DisplayName.Should().Be("Bcc Person");
    }

    [Fact]
    public void should_map_both_html_and_text_when_both_set()
    {
        // given
        var request = _RequestWithBody(html: "<p>Html</p>", text: "Text");

        // when
        var message = AzureCommunicationEmailMessageMapper.ToEmailMessage(request);

        // then
        message.Content.Html.Should().Be("<p>Html</p>");
        message.Content.PlainText.Should().Be("Text");
    }

    [Fact]
    public void should_leave_plaintext_null_when_only_html_set()
    {
        // given
        var request = _RequestWithBody(html: "<p>Html</p>", text: null);

        // when
        var message = AzureCommunicationEmailMessageMapper.ToEmailMessage(request);

        // then
        message.Content.Html.Should().Be("<p>Html</p>");
        message.Content.PlainText.Should().BeNull();
    }

    [Fact]
    public void should_leave_html_null_when_only_text_set()
    {
        // given
        var request = _RequestWithBody(html: null, text: "Text");

        // when
        var message = AzureCommunicationEmailMessageMapper.ToEmailMessage(request);

        // then
        message.Content.PlainText.Should().Be("Text");
        message.Content.Html.Should().BeNull();
    }

    [Fact]
    public void should_derive_known_attachment_content_type_and_round_trip_bytes()
    {
        // given
        var bytes = Encoding.UTF8.GetBytes("pdf-bytes");
        var request = _RequestWithAttachment("report.pdf", bytes);

        // when
        var message = AzureCommunicationEmailMessageMapper.ToEmailMessage(request);

        // then
        message.Attachments.Should().ContainSingle();
        message.Attachments[0].Name.Should().Be("report.pdf");
        message.Attachments[0].ContentType.Should().Be("application/pdf");
        message.Attachments[0].Content.ToArray().Should().Equal(bytes);
    }

    [Fact]
    public void should_fall_back_to_octet_stream_for_unknown_attachment_extension()
    {
        // given
        var request = _RequestWithAttachment("data.zzz", [1, 2, 3]);

        // when
        var message = AzureCommunicationEmailMessageMapper.ToEmailMessage(request);

        // then
        message.Attachments[0].ContentType.Should().Be("application/octet-stream");
    }

    private static SendSingleEmailRequest _RequestWithBody(string? html, string? text)
    {
        return new SendSingleEmailRequest
        {
            From = new EmailRequestAddress("sender@example.com"),
            Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress("to@example.com")] },
            Subject = "Subject",
            MessageHtml = html,
            MessageText = text,
        };
    }

    private static SendSingleEmailRequest _RequestWithAttachment(string name, byte[] file)
    {
        return new SendSingleEmailRequest
        {
            From = new EmailRequestAddress("sender@example.com"),
            Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress("to@example.com")] },
            Subject = "Subject",
            MessageText = "body",
            Attachments = [new EmailRequestAttachment { Name = name, File = file }],
        };
    }
}
