// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails;

/// <summary>
/// Provider-agnostic contract for sending a single email message.
/// </summary>
/// <remarks>
/// Delivery problems are reported through the returned <see cref="SendSingleEmailResponse"/>
/// rather than thrown, so callers should inspect <see cref="SendSingleEmailResponse.Success"/>
/// to detect failures. Implementations may throw for unrecoverable infrastructure faults
/// (for example SMTP authentication failure or AWS SES hard rejections) — see each
/// implementation for the specific exceptions that propagate.
/// </remarks>
[PublicAPI]
public interface IEmailSender
{
    /// <summary>
    /// Sends a single email message as described by <paramref name="request"/>.
    /// </summary>
    /// <param name="request">The email message to send, including sender, recipients, subject, and body.</param>
    /// <param name="cancellationToken">Token used to cancel the send operation.</param>
    /// <returns>
    /// A response indicating whether the message was delivered successfully. On failure,
    /// <see cref="SendSingleEmailResponse.FailureError"/> carries a human-readable description.
    /// </returns>
    ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    );
}
