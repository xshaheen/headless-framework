// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails;

/// <summary>
/// An email that a development sender captured instead of delivering, as read from <see cref="IEmailCaptureStore"/>.
/// </summary>
/// <remarks>
/// The capture is a snapshot: the recipient, attachment, and body values are copied when the send runs, so a caller
/// that changes its request afterwards does not change what the store shows.
/// </remarks>
[PublicAPI]
public sealed record CapturedEmail
{
    /// <summary>Gets the capture identifier, unique within the process and ordered by capture time.</summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the store <see cref="IEmailCaptureStore.Version"/> at which this email was captured. Pass it as
    /// <see cref="EmailCaptureQuery.AfterSequence"/> to read only the emails captured after it.
    /// </summary>
    public long Sequence { get; init; }

    /// <summary>Gets the time the send ran, read from the registered <see cref="TimeProvider"/>.</summary>
    public DateTimeOffset CapturedAt { get; init; }

    /// <summary>
    /// Gets the name of the sender instance that captured the email: the <c>AddNamed</c> name, or an empty string for
    /// the default (unnamed) sender.
    /// </summary>
    public required string SenderName { get; init; }

    /// <summary>Gets the sender address.</summary>
    public required EmailRequestAddress From { get; init; }

    /// <summary>Gets the primary recipients.</summary>
    public required IReadOnlyList<EmailRequestAddress> To { get; init; }

    /// <summary>Gets the carbon copy recipients.</summary>
    public IReadOnlyList<EmailRequestAddress> Cc { get; init; } = [];

    /// <summary>Gets the blind carbon copy recipients.</summary>
    public IReadOnlyList<EmailRequestAddress> Bcc { get; init; } = [];

    /// <summary>Gets the subject line.</summary>
    public required string Subject { get; init; }

    /// <summary>Gets the HTML body, or <see langword="null"/> when the request had none.</summary>
    public string? HtmlBody { get; init; }

    /// <summary>Gets the plain-text body, or <see langword="null"/> when the request had none.</summary>
    public string? TextBody { get; init; }

    /// <summary>Gets the attachment metadata, and the content of the attachments small enough to keep.</summary>
    public IReadOnlyList<CapturedEmailAttachment> Attachments { get; init; } = [];

    /// <summary>
    /// Gets why the send failed, for example a request with no body, or <see langword="null"/> when the sender
    /// accepted it.
    /// </summary>
    public string? FailureError { get; init; }

    /// <summary>Gets a value indicating whether the sender accepted the email.</summary>
    [MemberNotNullWhen(false, nameof(FailureError))]
    public bool Succeeded => FailureError is null;

    /// <summary>
    /// Gets the W3C trace id of the <see cref="System.Diagnostics.Activity"/> that was current during the send, or
    /// <see langword="null"/> when none was.
    /// </summary>
    public string? TraceId { get; init; }

    /// <summary>
    /// Gets the correlation id that the registered <c>ICorrelationIdProvider</c> reported during the send, or
    /// <see langword="null"/> when none is registered or active.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Serializes the email as an RFC 5322 MIME message, the content of an <c>.eml</c> file that mail clients open.
    /// </summary>
    /// <returns>The message text, with CRLF line endings.</returns>
    /// <remarks>
    /// Bodies and kept attachments are base64-encoded UTF-8. The <c>Bcc</c> header is written, so the file shows
    /// every recipient. An attachment whose content was not kept is listed in an
    /// <c>X-Headless-Attachment-Omitted</c> header instead of an empty part.
    /// </remarks>
    public string ToEml()
    {
        return EmlWriter.Write(this);
    }
}

/// <summary>The metadata of an attachment on a <see cref="CapturedEmail"/>, and its content when it was kept.</summary>
[PublicAPI]
public sealed record CapturedEmailAttachment
{
    /// <summary>Gets the file name displayed to the recipient.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the MIME content type the request gave, or <see langword="null"/> when it gave none.</summary>
    public string? ContentType { get; init; }

    /// <summary>Gets the size of the attachment content in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Gets a copy of the attachment content, or <see langword="null"/> when the content was larger than the
    /// sender's capture limit and only the metadata was kept.
    /// </summary>
    public ReadOnlyMemory<byte>? Content { get; init; }
}
