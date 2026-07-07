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
    /// <para>
    /// Only two categories of exception propagate instead of becoming a failed response: argument-validation
    /// exceptions for a malformed <paramref name="request"/> (a caller bug), and
    /// <see cref="OperationCanceledException"/> when the send is canceled. Every other failure is returned as
    /// <see cref="SendSingleSmsResponse.Failed"/>.
    /// </para>
    /// <para>
    /// <b>Error model (return, not throw):</b> a rejected or undeliverable send is ordinary, expected data — the
    /// vendor routinely rejects individual numbers — so it is surfaced as a
    /// <see cref="SendSingleSmsResponse.Failed"/> result carrying a <see cref="SmsFailureKind"/> the caller can
    /// branch on for retry/routing, not as an exception. This is the deliberate opposite of
    /// <c>ICaptchaVerifier.VerifyAsync</c>, which <em>throws</em> on the same class of transport failure because an
    /// unverifiable captcha challenge is exceptional (a bug or outage) rather than a routine negative outcome.
    /// </para>
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
