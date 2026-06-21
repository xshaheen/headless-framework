// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Emails;

/// <summary>
/// Describes a single email message to be sent via <see cref="IEmailSender"/>.
/// </summary>
/// <remarks>
/// At least one of <see cref="MessageHtml"/> or <see cref="MessageText"/> should be set;
/// some providers (for example MailKit/SMTP) enforce this and throw
/// <see cref="InvalidOperationException"/> when both are <see langword="null"/>.
/// </remarks>
[PublicAPI]
public sealed record SendSingleEmailRequest
{
    /// <summary>The sender address shown in the email "From" header.</summary>
    public required EmailRequestAddress From { get; init; }

    /// <summary>The recipients of the email message.</summary>
    public required EmailRequestDestination Destination { get; init; }

    /// <summary>The subject line of the email.</summary>
    public required string Subject { get; init; }

    /// <summary>
    /// The HTML body of the email. When both <see cref="MessageHtml"/> and
    /// <see cref="MessageText"/> are provided, clients that support HTML will prefer this.
    /// </summary>
    public string? MessageHtml { get; init; }

    /// <summary>
    /// The plain-text body of the email, used as a fallback when the recipient's client
    /// does not render HTML.
    /// </summary>
    public string? MessageText { get; init; }

    /// <summary>The attachments to include in the email. Defaults to an empty list.</summary>
    public IReadOnlyList<EmailRequestAttachment> Attachments { get; init; } = [];
}

/// <summary>
/// An email address optionally paired with a display name.
/// </summary>
/// <param name="EmailAddress">The RFC 5321 email address (e.g. <c>user@example.com</c>).</param>
/// <param name="DisplayName">
/// The human-readable name shown alongside the address (e.g. <c>Alice</c>).
/// When <see langword="null"/>, only the bare address is used.
/// </param>
public sealed record EmailRequestAddress(string EmailAddress, string? DisplayName = null)
{
    /// <summary>
    /// Implicitly converts a plain email address string to an <see cref="EmailRequestAddress"/>
    /// with no display name.
    /// </summary>
    public static implicit operator EmailRequestAddress(string operand) => new(operand);

    /// <summary>
    /// Converts a plain email address string to an <see cref="EmailRequestAddress"/>
    /// with no display name.
    /// </summary>
    public static EmailRequestAddress FromString(string operand) => operand;

    /// <summary>
    /// Returns <c>"DisplayName &lt;EmailAddress&gt;"</c> when a display name is set;
    /// otherwise returns the bare <see cref="EmailAddress"/>.
    /// </summary>
    public override string ToString()
    {
        return DisplayName is null ? EmailAddress : $"{DisplayName} <{EmailAddress}>";
    }
}

/// <summary>
/// The set of recipient addresses for an outgoing email.
/// </summary>
public sealed class EmailRequestDestination
{
    /// <summary>The primary recipients (To line).</summary>
    public required IReadOnlyList<EmailRequestAddress> ToAddresses { get; init; }

    /// <summary>Blind-carbon-copy recipients. Defaults to an empty list.</summary>
    public IReadOnlyList<EmailRequestAddress> BccAddresses { get; init; } = [];

    /// <summary>Carbon-copy recipients. Defaults to an empty list.</summary>
    public IReadOnlyList<EmailRequestAddress> CcAddresses { get; init; } = [];
}

/// <summary>
/// A binary file attachment to include in an outgoing email.
/// </summary>
public sealed class EmailRequestAttachment
{
    /// <summary>The file name shown to the recipient (e.g. <c>invoice.pdf</c>).</summary>
    public required string Name { get; init; }

    /// <summary>The raw file bytes to attach.</summary>
    public required byte[] File { get; init; }
}
