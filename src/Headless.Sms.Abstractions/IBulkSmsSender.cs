// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms;

/// <summary>
/// Defines bulk SMS delivery capability to send one message to multiple recipients in a single provider call.
/// </summary>
/// <remarks>
/// Resolve directly from dependency injection or cast an <see cref="ISmsSender"/> instance.
/// Providers without bulk support, such as Twilio or AWS SNS, do not implement this interface.
/// </remarks>
[PublicAPI]
public interface IBulkSmsSender
{
    /// <summary>Sends the specified bulk SMS request to all designated recipients.</summary>
    /// <param name="request">The message, recipients, and optional metadata to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task containing a <see cref="SendBulkSmsResponse"/> with one result per recipient.
    /// Remote provider and transport failures return failed results rather than throwing exceptions.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="request"/> or <see cref="SendBulkSmsRequest.Destinations"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="request"/> has no destinations or an empty message body.
    /// </exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    ValueTask<SendBulkSmsResponse> SendBulkAsync(
        SendBulkSmsRequest request,
        CancellationToken cancellationToken = default
    );
}
