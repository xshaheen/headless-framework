// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails;

/// <summary>
/// Defines a provider-agnostic contract for sending an email message.
/// </summary>
/// <remarks>
/// Delivery failures and transport faults are reported through the returned <see cref="SendSingleEmailResponse"/>
/// rather than thrown as exceptions. Implementations throw only for operation cancellation or invalid request data.
/// </remarks>
[PublicAPI]
public interface IEmailSender
{
    /// <summary>
    /// Sends a single email message.
    /// </summary>
    /// <param name="request">The email message to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation, containing the send outcome response.</returns>
    ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    );
}
