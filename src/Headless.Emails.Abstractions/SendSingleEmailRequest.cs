// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails;

/// <summary>
/// Describes a single email message sent through <see cref="IEmailSender"/>.
/// </summary>
/// <remarks>
/// At least one of <see cref="MessageHtml"/> or <see cref="MessageText"/> must be set.
/// Providers call <see cref="EnsureHasBody"/> before sending and throw
/// <see cref="InvalidOperationException"/> when both properties are empty or white space.
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
    /// Gets the HTML body of the email.
    /// </summary>
    public string? MessageHtml { get; init; }

    /// <summary>
    /// Gets the plain text body of the email.
    /// </summary>
    public string? MessageText { get; init; }

    /// <summary>Gets the attachments to include in the email.</summary>
    public IReadOnlyList<EmailRequestAttachment> Attachments { get; init; } = [];

    /// <summary>
    /// Validates that at least one of <see cref="MessageHtml"/> or <see cref="MessageText"/> contains content.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Both <see cref="MessageHtml"/> and <see cref="MessageText"/> are <see langword="null"/>, empty, or contain only white space.
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
/// <param name="EmailAddress">The RFC 5321 email address.</param>
/// <param name="DisplayName">The display name shown alongside the address.</param>
[PublicAPI]
public sealed record EmailRequestAddress(string EmailAddress, string? DisplayName = null)
{
    /// <summary>
    /// Implicitly converts an email address string to an <see cref="EmailRequestAddress"/>.
    /// </summary>
    /// <param name="operand">The email address string.</param>
    public static implicit operator EmailRequestAddress(string operand) => new(operand);

    /// <summary>
    /// Converts an email address string to an <see cref="EmailRequestAddress"/>.
    /// </summary>
    /// <param name="operand">The email address string.</param>
    /// <returns>A new <see cref="EmailRequestAddress"/> instance.</returns>
    public static EmailRequestAddress FromString(string operand)
    {
        return operand;
    }

    /// <summary>
    /// Formats the address as a string, including the display name when present.
    /// </summary>
    /// <returns>A formatted email address string.</returns>
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
    /// <summary>Gets the primary recipient addresses.</summary>
    public required IReadOnlyList<EmailRequestAddress> ToAddresses { get; init; }

    /// <summary>Gets the blind carbon copy recipient addresses.</summary>
    public IReadOnlyList<EmailRequestAddress> BccAddresses { get; init; } = [];

    /// <summary>Gets the carbon copy recipient addresses.</summary>
    public IReadOnlyList<EmailRequestAddress> CcAddresses { get; init; } = [];
}

/// <summary>
/// Represents a binary file attachment in an outgoing email.
/// </summary>
[PublicAPI]
public sealed record EmailRequestAttachment
{
    /// <summary>Gets the file name displayed to the recipient.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the raw file content bytes.</summary>
    public required ReadOnlyMemory<byte> File { get; init; }

    /// <summary>
    /// Gets the MIME content type of the attachment.
    /// </summary>
    public string? ContentType { get; init; }
}
