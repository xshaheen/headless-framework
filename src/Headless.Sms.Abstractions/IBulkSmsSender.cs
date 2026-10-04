// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Sms;

/// <summary>
/// Defines the optional bulk capability: delivering one message to many recipients in a single provider call.
/// </summary>
/// <remarks>
/// Resolve it from dependency injection directly, or check <c>sender is IBulkSmsSender</c> on an
/// <see cref="ISmsSender"/>. Providers without native bulk support (for example Twilio and AWS SNS, which
/// send to one recipient per API call) do not register or implement this interface, so resolving it fails
/// and signals that the capability is unavailable. To fan a message out over such a provider, loop
/// <see cref="ISmsSender.SendAsync"/>.
/// </remarks>
[PublicAPI]
public interface IBulkSmsSender
{
    /// <summary>Sends the message described by <paramref name="request"/> to all of its recipients.</summary>
    /// <param name="request">The message, recipients, and optional metadata to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="SendBulkSmsResponse"/> carrying one <see cref="SmsRecipientResult"/> per recipient.
    /// As with <see cref="ISmsSender"/>, provider and transport failures are returned rather than thrown.
    /// Providers whose API reports a single status for the whole batch apply that aggregate outcome to
    /// every recipient.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="request"/> or <see cref="SendBulkSmsRequest.Destinations"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="request"/> has no destinations or an empty message body.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// The operation was canceled through <paramref name="cancellationToken"/>.
    /// </exception>
    ValueTask<SendBulkSmsResponse> SendBulkAsync(
        SendBulkSmsRequest request,
        CancellationToken cancellationToken = default
    );
}
