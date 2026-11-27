// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using MimeKit;

namespace Headless.Emails.Dev;

/// <summary>Writes a <see cref="CapturedEmail"/> as MIME through the converter the real providers send with.</summary>
internal static class CapturedEmailMime
{
    public static async Task WriteAsync(CapturedEmail email, Stream destination, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(destination);

        var request = new SendSingleEmailRequest
        {
            From = _Exportable(email.From),
            Destination = new EmailRequestDestination
            {
                ToAddresses = [.. email.To.Select(_Exportable)],
                CcAddresses = [.. email.Cc.Select(_Exportable)],
                BccAddresses = [.. email.Bcc.Select(_Exportable)],
            },
            Subject = email.Subject,
            MessageHtml = email.HtmlBody,
            MessageText = email.TextBody,
            Attachments =
            [
                .. email
                    .Attachments.Where(static a => a.Content.HasValue)
                    .Select(static a => new EmailRequestAttachment
                    {
                        Name = string.IsNullOrWhiteSpace(a.FileName) ? "attachment" : a.FileName,
                        File = a.Content!.Value,
                        ContentType = _ValidContentTypeOrNull(a.ContentType),
                    }),
            ],
        };

        using var message = await request.ConvertToMimeMessageAsync(cancellationToken).ConfigureAwait(false);

        message.Date = email.CapturedAt;
        message.MessageId = $"{email.Id:N}@dev-inbox.headless";

        if (email.SenderName.Length > 0)
        {
            message.Headers.Add("X-Headless-Sender", email.SenderName);
        }

        if (email.TraceId is not null)
        {
            message.Headers.Add("X-Headless-Trace-Id", email.TraceId);
        }

        if (email.CorrelationId is not null)
        {
            message.Headers.Add("X-Headless-Correlation-Id", email.CorrelationId);
        }

        if (email.FailureError is not null)
        {
            message.Headers.Add("X-Headless-Failure", email.FailureError);
        }

        foreach (var omitted in email.Attachments.Where(static a => !a.Content.HasValue))
        {
            message.Headers.Add(
                "X-Headless-Attachment-Omitted",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{omitted.FileName}; size={omitted.SizeBytes}; type={_ValidContentTypeOrNull(omitted.ContentType) ?? "unknown"}"
                )
            );
        }

        // Picks transfer encodings that keep every line within the RFC 5322 limit, whatever the bodies hold.
        message.Prepare(EncodingConstraint.SevenBit, maxLineLength: 998);

        // RFC 5322 requires CRLF; MimeKit's default writes the platform newline, which is LF outside Windows.
        var format = FormatOptions.Default.Clone();
        format.NewLineFormat = NewLineFormat.Dos;

        await message.WriteToAsync(format, destination, cancellationToken).ConfigureAwait(false);
    }

    // A captured address is whatever the caller sent, so one MimeKit cannot parse (a typo, or a line break) would fail
    // the whole export. Such an address becomes a reserved placeholder that keeps the original text as its name.
    private static EmailRequestAddress _Exportable(EmailRequestAddress address)
    {
        if (
            MailboxAddress.TryParse(address.EmailAddress, out var parsed)
            && string.Equals(parsed.Address, address.EmailAddress, StringComparison.Ordinal)
        )
        {
            return address;
        }

        var original = string.Concat(address.EmailAddress.Where(static c => !char.IsControl(c)));
        var name = address.DisplayName is null ? original : $"{address.DisplayName} ({original})";

        return new EmailRequestAddress(_InvalidAddressPlaceholder, name);
    }

    private const string _InvalidAddressPlaceholder = "invalid-address@dev-inbox.invalid";

    // A caller's content type is unvalidated; one MimeKit cannot parse would fail the export, so fall back to the
    // type the file name implies.
    private static string? _ValidContentTypeOrNull(string? contentType)
    {
        return contentType is { Length: > 0 } && ContentType.TryParse(contentType, out _) ? contentType : null;
    }
}
