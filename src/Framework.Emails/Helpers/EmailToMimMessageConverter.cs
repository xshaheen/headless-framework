// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Emails.Contracts;
using MimeKit;

namespace Framework.Emails.Helpers;

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

        var fromAddress = new MailboxAddress(
            name: request.From.DisplayName ?? request.From.EmailAddress,
            address: request.From.EmailAddress
        );

        message.From.Add(fromAddress);

        foreach (var to in request.Destination.ToAddresses)
        {
            var toAddress = new MailboxAddress(name: to.DisplayName ?? to.EmailAddress, address: to.EmailAddress);
            message.To.Add(toAddress);
        }

        foreach (var cc in request.Destination.CcAddresses)
        {
            var ccAddress = new MailboxAddress(name: cc.DisplayName ?? cc.EmailAddress, address: cc.EmailAddress);
            message.Cc.Add(ccAddress);
        }

        foreach (var bcc in request.Destination.BccAddresses)
        {
            var bccAddress = new MailboxAddress(name: bcc.DisplayName ?? bcc.EmailAddress, address: bcc.EmailAddress);
            message.Bcc.Add(bccAddress);
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
}
