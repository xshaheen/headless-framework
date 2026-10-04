// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails;

/// <summary>
/// Defines a provider-agnostic contract for sending an email message.
/// </summary>
/// <remarks>
/// Delivery problems — provider rejections and transport faults alike — are reported through the returned
/// <see cref="SendSingleEmailResponse"/> rather than thrown, so callers detect failures by inspecting
/// <see cref="SendSingleEmailResponse.Success"/>. Implementations throw only for cancellation
/// (<see cref="OperationCanceledException"/>) and argument validation (for example a body-less request rejected
/// by <see cref="SendSingleEmailRequest.EnsureHasBody"/>); they never surface a provider or SMTP/SES/ACS error
/// as an exception.
/// </remarks>
[PublicAPI]
public interface IEmailSender
{
    /// <summary>
    /// Sends a single email message.
    /// </summary>
    /// <param name="request">The email message to send, including sender, recipients, subject, and body.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A response indicating whether the message was accepted for delivery. On success,
    /// <see cref="SendSingleEmailResponse.ProviderMessageId"/> carries the backend's message identifier when the
    /// backend returns one; on failure, <see cref="SendSingleEmailResponse.FailureError"/> carries a
    /// human-readable description.
    /// </returns>
    ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    );
}
