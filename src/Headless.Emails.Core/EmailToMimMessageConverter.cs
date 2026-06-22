// Copyright (c) Mahmoud Shaheen. All rights reserved.

using MimeKit;

namespace Headless.Emails;

/// <summary>
/// Converts <see cref="SendSingleEmailRequest"/> instances to MimeKit <see cref="MimeMessage"/> objects.
/// </summary>
public static class EmailToMimMessageConverter
{
    /// <summary>
    /// Converts a <see cref="SendSingleEmailRequest"/> into a MimeKit <see cref="MimeMessage"/>,
    /// including headers, body parts, and attachments.
    /// </summary>
    /// <param name="request">The email request to convert.</param>
    /// <param name="cancellationToken">Token used to cancel async attachment loading.</param>
    /// <returns>
    /// A fully-populated <see cref="MimeMessage"/>. The caller is responsible for disposing it.
    /// </returns>
    /// <remarks>
    /// Attachments are streamed asynchronously from their byte arrays. If an exception is thrown
    /// during construction the partially-built <see cref="MimeMessage"/> is disposed before
    /// the exception propagates.
    /// </remarks>
    public static async Task<MimeMessage> ConvertToMimeMessageAsync(
        this SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var message = new MimeMessage();

        try
        {
            await message._BuildMimeMessageAsync(request, cancellationToken).ConfigureAwait(false);
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
            await emailBuilder
                .Attachments.AddAsync(requestAttachment.Name, fileStream, cancellationToken)
                .ConfigureAwait(false);
        }

        message.Body = emailBuilder.ToMessageBody();
    }

    /// <summary>
    /// Maps an <see cref="EmailRequestAddress"/> to a MimeKit <see cref="MailboxAddress"/>.
    /// </summary>
    /// <param name="address">The address to map.</param>
    /// <returns>
    /// A <see cref="MailboxAddress"/> whose display name falls back to the bare email address
    /// when <see cref="EmailRequestAddress.DisplayName"/> is <see langword="null"/>.
    /// </returns>
    public static MailboxAddress MapToMailboxAddress(this EmailRequestAddress address)
    {
        return new MailboxAddress(address.DisplayName ?? address.EmailAddress, address.EmailAddress);
    }
}
