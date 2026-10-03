// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
