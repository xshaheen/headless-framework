// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using MimeKit;

namespace Headless.Emails;

/// <summary>
/// Converts <see cref="SendSingleEmailRequest"/> instances to MimeKit <see cref="MimeMessage"/> objects.
/// Internal to the email providers (MailKit and AWS SES); application code does not call this directly.
/// </summary>
internal static class EmailToMimeMessageConverter
{
    /// <summary>
    /// Converts an email request into a MimeKit <see cref="MimeMessage"/>, including headers, body parts,
    /// and attachments.
    /// </summary>
    /// <param name="request">The email request to convert.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous attachment loading.</param>
    /// <returns>
    /// A fully populated <see cref="MimeMessage"/>. The caller is responsible for disposing it.
    /// </returns>
    /// <remarks>
    /// A binary attachment's part reads the caller's memory when the message is written, so that memory must stay
    /// unchanged until then; text and message attachments are copied in. When construction throws, the partially
    /// built <see cref="MimeMessage"/> is disposed before the exception propagates.
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

        if (!string.IsNullOrWhiteSpace(request.MessageText))
        {
            emailBuilder.TextBody = request.MessageText;
        }

        if (!string.IsNullOrWhiteSpace(request.MessageHtml))
        {
            emailBuilder.HtmlBody = request.MessageHtml;
        }

        foreach (var requestAttachment in request.Attachments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var contentType = ContentType.Parse(
                requestAttachment.ContentType is { Length: > 0 } explicitType
                    ? explicitType
                    : EmailAttachmentContentType.Resolve(requestAttachment.Name)
            );

            if (contentType.IsMimeType("text", "*") || contentType.IsMimeType("message", "*"))
            {
                // MimeKit parses a message attachment and picks a text attachment's charset and encoding from its
                // content, which needs its own loaded copy, so those keep the collection's loading path.
                await using var fileStream = _OpenAttachmentStream(requestAttachment.File);

                await emailBuilder
                    .Attachments.AddAsync(requestAttachment.Name, fileStream, contentType, cancellationToken)
                    .ConfigureAwait(false);

                continue;
            }

            // A binary attachment is base64 in practice, so naming the encoding up front lets the part read the
            // caller's bytes at write time instead of MimeKit copying them into its own buffer and scanning that copy
            // to choose the same encoding. The message owns the stream and disposes it with the part.
            emailBuilder.Attachments.Add(
                new MimePart(contentType)
                {
                    Content = new MimeContent(_OpenAttachmentStream(requestAttachment.File)),
                    ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                    ContentTransferEncoding = ContentEncoding.Base64,
                    FileName = requestAttachment.Name,
                }
            );
        }

        message.Body = emailBuilder.ToMessageBody();
    }

    /// <summary>
    /// Exposes an attachment's bytes as a stream for MimeKit to read. When the memory is array-backed (the
    /// normal case) the array is wrapped read-only rather than copied, so a binary attachment is encoded
    /// straight from the caller's bytes when the message is written.
    /// </summary>
    private static MemoryStream _OpenAttachmentStream(ReadOnlyMemory<byte> file)
    {
        if (MemoryMarshal.TryGetArray(file, out var segment) && segment.Array is not null)
        {
            return new MemoryStream(
                segment.Array,
                segment.Offset,
                segment.Count,
                writable: false,
                publiclyVisible: true
            );
        }

        // Not array-backed (native or otherwise unmanaged memory): there is no array to wrap, so copy.
        var copy = new MemoryStream(file.Length);
        copy.Write(file.Span);
        copy.Position = 0;

        return copy;
    }

    /// <summary>
    /// Maps an email request address to a MimeKit <see cref="MailboxAddress"/>.
    /// </summary>
    /// <param name="address">The email request address to map.</param>
    /// <returns>
    /// A <see cref="MailboxAddress"/> whose display name falls back to the bare email address when
    /// <see cref="EmailRequestAddress.DisplayName"/> is <see langword="null"/>.
    /// </returns>
    public static MailboxAddress MapToMailboxAddress(this EmailRequestAddress address)
    {
        return new MailboxAddress(address.DisplayName ?? address.EmailAddress, address.EmailAddress);
    }
}
