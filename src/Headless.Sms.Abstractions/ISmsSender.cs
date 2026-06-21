// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms;

/// <summary>Sends SMS messages through a configured provider backend.</summary>
[PublicAPI]
public interface ISmsSender
{
    /// <summary>Sends an SMS message described by <paramref name="request"/>.</summary>
    /// <param name="request">The message, destination(s), and optional metadata to send.</param>
    /// <param name="cancellationToken">A token to cancel the send.</param>
    /// <returns>
    /// A <see cref="SendSingleSmsResponse"/> describing the outcome. Implementations return
    /// <see cref="SendSingleSmsResponse.Failed"/> for every provider and transport failure
    /// (network errors, non-success status codes, vendor rejections, malformed responses) rather
    /// than throwing. Only <see cref="OperationCanceledException"/> is allowed to propagate.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// The send was canceled through <paramref name="cancellationToken"/>.
    /// </exception>
    ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    );
}
