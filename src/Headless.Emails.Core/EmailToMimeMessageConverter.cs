// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using MimeKit;

namespace Headless.Emails;

/// <summary>
/// Converts <see cref="SendSingleEmailRequest"/> instances to MimeKit <see cref="MimeMessage"/> objects.
/// </summary>
internal static class EmailToMimeMessageConverter
{
    /// <summary>
    /// Converts an email request into a MimeKit message.
    /// </summary>
    /// <param name="request">The email request to convert.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation, containing the constructed <see cref="MimeMessage"/>.</returns>
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
            await using var fileStream = _OpenAttachmentStream(requestAttachment.File);

            if (requestAttachment.ContentType is { Length: > 0 } contentType)
            {
                await emailBuilder
                    .Attachments.AddAsync(
                        requestAttachment.Name,
                        fileStream,
                        ContentType.Parse(contentType),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            else
            {
                await emailBuilder
                    .Attachments.AddAsync(requestAttachment.Name, fileStream, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        message.Body = emailBuilder.ToMessageBody();
    }

    /// <summary>
    /// Wraps array-backed memory in a stream without copying, or copies non-array memory to a stream.
    /// </summary>
    private static MemoryStream _OpenAttachmentStream(ReadOnlyMemory<byte> file)
    {
        if (MemoryMarshal.TryGetArray(file, out var segment) && segment.Array is not null)
        {
            return new MemoryStream(segment.Array, segment.Offset, segment.Count, writable: false);
        }

        var copy = new MemoryStream(file.Length);
        copy.Write(file.Span);
        copy.Position = 0;

        return copy;
    }

    /// <summary>
    /// Maps an email request address to a MimeKit mailbox address.
    /// </summary>
    /// <param name="address">The email request address to map.</param>
    /// <returns>A mapped <see cref="MailboxAddress"/> instance.</returns>
    public static MailboxAddress MapToMailboxAddress(this EmailRequestAddress address)
    {
        return new MailboxAddress(address.DisplayName ?? address.EmailAddress, address.EmailAddress);
    }
}
