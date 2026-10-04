// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails;

/// <summary>
/// Describes a single email message sent through <see cref="IEmailSender"/>.
/// </summary>
/// <remarks>
/// At least one of <see cref="MessageHtml"/> or <see cref="MessageText"/> must be set. All providers call
/// <see cref="EnsureHasBody"/> before sending and throw <see cref="InvalidOperationException"/> when both are
/// <see langword="null"/> or white space, so a body-less request is rejected identically regardless of backend.
/// </remarks>
[PublicAPI]
public sealed record SendSingleEmailRequest
{
    /// <summary>Gets the sender address displayed in the email From header.</summary>
    public required EmailRequestAddress From { get; init; }

    /// <summary>Gets the recipients of the email message.</summary>
    public required EmailRequestDestination Destination { get; init; }

    /// <summary>Gets the subject line of the email.</summary>
    public required string Subject { get; init; }

    /// <summary>
    /// Gets the HTML body of the email. When both <see cref="MessageHtml"/> and <see cref="MessageText"/> are
    /// provided, clients that support HTML prefer this body.
    /// </summary>
    public string? MessageHtml { get; init; }

    /// <summary>
    /// Gets the plain-text body of the email, used as a fallback when the recipient's client does not
    /// render HTML.
    /// </summary>
    public string? MessageText { get; init; }

    /// <summary>Gets the attachments to include in the email. Defaults to an empty list.</summary>
    public IReadOnlyList<EmailRequestAttachment> Attachments { get; init; } = [];

    /// <summary>
    /// Validates that at least one of <see cref="MessageHtml"/> or <see cref="MessageText"/> contains content.
    /// All providers invoke this before sending, so a body-less request is rejected identically regardless
    /// of backend.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Both <see cref="MessageHtml"/> and <see cref="MessageText"/> are <see langword="null"/>, empty, or
    /// contain only white space.
    /// </exception>
    public void EnsureHasBody()
    {
        if (string.IsNullOrWhiteSpace(MessageHtml) && string.IsNullOrWhiteSpace(MessageText))
        {
            throw new InvalidOperationException("At least one of MessageHtml or MessageText must be provided.");
        }
    }
}

/// <summary>
/// Represents an email address and an optional display name.
/// </summary>
/// <param name="EmailAddress">The RFC 5321 email address, for example <c>user@example.com</c>.</param>
/// <param name="DisplayName">
/// The display name shown alongside the address, for example <c>Alice</c>. When <see langword="null"/>,
/// only the bare address is used.
/// </param>
[PublicAPI]
public sealed record EmailRequestAddress(string EmailAddress, string? DisplayName = null)
{
    /// <summary>
    /// Implicitly converts an email address string to an <see cref="EmailRequestAddress"/> with no
    /// display name.
    /// </summary>
    /// <param name="operand">The email address string.</param>
    public static implicit operator EmailRequestAddress(string operand) => new(operand);

    /// <summary>
    /// Converts an email address string to an <see cref="EmailRequestAddress"/> with no display name.
    /// </summary>
    /// <param name="operand">The email address string.</param>
    /// <returns>A new <see cref="EmailRequestAddress"/> instance.</returns>
    public static EmailRequestAddress FromString(string operand)
    {
        return operand;
    }

    /// <summary>
    /// Formats the address as <c>"DisplayName &lt;EmailAddress&gt;"</c> when a display name is set;
    /// otherwise returns the bare <see cref="EmailAddress"/>.
    /// </summary>
    /// <returns>The formatted address string.</returns>
    public override string ToString()
    {
        return DisplayName is null ? EmailAddress : $"{DisplayName} <{EmailAddress}>";
    }
}

/// <summary>
/// Represents recipient addresses for an outgoing email.
/// </summary>
[PublicAPI]
public sealed record EmailRequestDestination
{
    /// <summary>Gets the primary recipient addresses (To line).</summary>
    public required IReadOnlyList<EmailRequestAddress> ToAddresses { get; init; }

    /// <summary>Gets the blind carbon copy recipient addresses. Defaults to an empty list.</summary>
    public IReadOnlyList<EmailRequestAddress> BccAddresses { get; init; } = [];

    /// <summary>Gets the carbon copy recipient addresses. Defaults to an empty list.</summary>
    public IReadOnlyList<EmailRequestAddress> CcAddresses { get; init; } = [];
}

/// <summary>
/// Represents a binary file attachment in an outgoing email.
/// </summary>
[PublicAPI]
public sealed record EmailRequestAttachment
{
    /// <summary>Gets the file name displayed to the recipient, for example <c>invoice.pdf</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the raw file content bytes.</summary>
    public required ReadOnlyMemory<byte> File { get; init; }

    /// <summary>
    /// Gets the MIME content type of the attachment, for example <c>application/pdf</c>. When
    /// <see langword="null"/>, providers that require an explicit content type infer it from the
    /// <see cref="Name"/> extension.
    /// </summary>
    public string? ContentType { get; init; }
}
