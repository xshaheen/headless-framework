// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails;

/// <summary>
/// Provider-agnostic contract for sending a single email message.
/// </summary>
/// <remarks>
/// Delivery problems — provider rejections and transport faults alike — are reported through the
/// returned <see cref="SendSingleEmailResponse"/> rather than thrown, so callers inspect
/// <see cref="SendSingleEmailResponse.Success"/> to detect failures. Implementations throw only for
/// cancellation (<see cref="OperationCanceledException"/>) and argument validation (for example a
/// body-less request via <see cref="SendSingleEmailRequest.EnsureHasBody"/>); they never surface a
/// provider or SMTP/SES/ACS error as an exception.
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
    /// A response indicating whether the message was accepted for delivery. On success,
    /// <see cref="SendSingleEmailResponse.ProviderMessageId"/> carries the backend's message id when it
    /// returns one; on failure, <see cref="SendSingleEmailResponse.FailureError"/> carries a human-readable
    /// description.
    /// </returns>
    ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    );
}
