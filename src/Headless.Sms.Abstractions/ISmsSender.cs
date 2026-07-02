// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms;

/// <summary>Sends SMS messages to a single recipient through a configured provider backend.</summary>
/// <remarks>
/// To send the same message to many recipients in one provider call, resolve <see cref="IBulkSmsSender"/>
/// (implemented by providers with native bulk support).
/// </remarks>
[PublicAPI]
public interface ISmsSender
{
    /// <summary>Sends an SMS message described by <paramref name="request"/> to its single recipient.</summary>
    /// <param name="request">The message, recipient, and optional metadata to send.</param>
    /// <param name="cancellationToken">A token to cancel the send.</param>
    /// <returns>
    /// A <see cref="SendSingleSmsResponse"/> describing the outcome. Implementations return
    /// <see cref="SendSingleSmsResponse.Failed"/> for every provider and transport failure
    /// (network errors, non-success status codes, vendor rejections, malformed responses) rather
    /// than throwing.
    /// </returns>
    /// <remarks>
    /// Only two categories of exception propagate instead of becoming a failed response: argument-validation
    /// exceptions for a malformed <paramref name="request"/> (a caller bug), and
    /// <see cref="OperationCanceledException"/> when the send is canceled. Every other failure is returned as
    /// <see cref="SendSingleSmsResponse.Failed"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="request"/> or its <see cref="SendSingleSmsRequest.Destination"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="request"/> has an empty message body.</exception>
    /// <exception cref="OperationCanceledException">
    /// The send was canceled through <paramref name="cancellationToken"/>.
    /// </exception>
    ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    );
}
