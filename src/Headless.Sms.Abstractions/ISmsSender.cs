// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms;

/// <summary>Sends SMS messages to a single recipient through a configured provider backend.</summary>
/// <remarks>
/// To send a message to multiple recipients in one call, resolve <see cref="IBulkSmsSender"/> when supported by the
/// provider.
/// </remarks>
[PublicAPI]
public interface ISmsSender
{
    /// <summary>Sends an SMS message to a single recipient.</summary>
    /// <param name="request">The message, recipient, and optional metadata to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task containing a <see cref="SendSingleSmsResponse"/> describing the outcome. Remote provider and transport
    /// failures return failed responses rather than throwing exceptions.
    /// </returns>
    /// <remarks>
    /// Malformed request arguments throw validation exceptions, and cancellation throws
    /// <see cref="OperationCanceledException"/>. Other failures return <see cref="SendSingleSmsResponse.Failed(string, SmsFailureKind)"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="request"/> or <see cref="SendSingleSmsRequest.Destination"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="request"/> has an empty message body.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    );
}
