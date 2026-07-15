// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Emails;

/// <summary>
/// Describes a single email message to be sent via <see cref="IEmailSender"/>.
/// </summary>
/// <remarks>
/// At least one of <see cref="MessageHtml"/> or <see cref="MessageText"/> must be set.
/// All providers call <see cref="EnsureHasBody"/> before sending and throw
/// <see cref="InvalidOperationException"/> when both are <see langword="null"/> or whitespace-only,
/// so the behavior is identical across backends.
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

    /// <summary>
    /// Validates that at least one of <see cref="MessageHtml"/> or <see cref="MessageText"/>
    /// carries content. All providers invoke this before sending so a body-less request is
    /// rejected identically regardless of backend.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when both <see cref="MessageHtml"/> and <see cref="MessageText"/> are
    /// <see langword="null"/> or whitespace-only.
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
/// An email address optionally paired with a display name.
/// </summary>
/// <param name="EmailAddress">The RFC 5321 email address (e.g. <c>user@example.com</c>).</param>
/// <param name="DisplayName">
/// The human-readable name shown alongside the address (e.g. <c>Alice</c>).
/// When <see langword="null"/>, only the bare address is used.
/// </param>
[PublicAPI]
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
    public static EmailRequestAddress FromString(string operand)
    {
        return operand;
    }

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
[PublicAPI]
public sealed record EmailRequestDestination
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
[PublicAPI]
public sealed record EmailRequestAttachment
{
    /// <summary>The file name shown to the recipient (e.g. <c>invoice.pdf</c>).</summary>
    public required string Name { get; init; }

    /// <summary>The raw file bytes to attach.</summary>
    public required ReadOnlyMemory<byte> File { get; init; }

    /// <summary>
    /// The MIME content type (e.g. <c>application/pdf</c>). When <see langword="null"/>,
    /// the type is inferred from the <see cref="Name"/> extension.
    /// </summary>
    public string? ContentType { get; init; }
}
