// Copyright (c) Mahmoud Shaheen. All rights reserved.

using MimeKit;

namespace Framework.Emails;

public static class EmailToMimMessageConverter
{
    public static async Task<MimeMessage> ConvertToMimeMessageAsync(
        this SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var message = new MimeMessage();

        try
        {
            await _BuildMimeMessageAsync(message, request, cancellationToken);
        }
        catch
        {
            message.Dispose();

            throw;
        }

        return message;
    }

    private static async Task _BuildMimeMessageAsync(
        this MimeMessage message,
        SendSingleEmailRequest request,
        CancellationToken cancellationToken
    )
    {
        message.Subject = request.Subject;
        message.From.Add(request.From.MapToMailboxAddress());

        foreach (var to in request.Destination.ToAddresses)
        {
            message.To.Add(to.MapToMailboxAddress());
        }

        foreach (var cc in request.Destination.CcAddresses)
        {
            message.Cc.Add(cc.MapToMailboxAddress());
        }

        foreach (var bcc in request.Destination.BccAddresses)
        {
            message.Bcc.Add(bcc.MapToMailboxAddress());
        }

        var emailBuilder = new BodyBuilder();

        if (request.MessageText is not null)
        {
            emailBuilder.TextBody = request.MessageText;
        }

        if (request.MessageHtml is not null)
        {
            emailBuilder.HtmlBody = request.MessageHtml;
        }

        foreach (var requestAttachment in request.Attachments)
        {
            var fileStream = new MemoryStream(requestAttachment.File);
            fileStream.Seek(0, SeekOrigin.Begin);
            await emailBuilder.Attachments.AddAsync(requestAttachment.Name, fileStream, cancellationToken);
        }

        message.Body = emailBuilder.ToMessageBody();
    }

    public static MailboxAddress MapToMailboxAddress(this EmailRequestAddress address)
    {
        return new MailboxAddress(address.DisplayName ?? address.EmailAddress, address.EmailAddress);
    }
}
