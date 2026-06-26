// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Sms;

/// <summary>Describes the outcome of sending a single SMS message.</summary>
/// <remarks>
/// A successful send may carry a provider-assigned <see cref="ProviderMessageId"/> when the backend
/// returns one. A failed send always carries a non-null <see cref="FailureError"/> together with a
/// <see cref="FailureKind"/> that classifies the failure for retry and provider-routing decisions.
/// </remarks>
[PublicAPI]
public sealed class SendSingleSmsResponse
{
    private SendSingleSmsResponse() { }

    /// <summary>Whether the message was accepted by the provider.</summary>
    [MemberNotNullWhen(false, nameof(FailureError))]
    public bool Success { get; private init; }

    /// <summary>
    /// Provider-assigned identifier for the accepted message when the backend returns one (for example the
    /// Twilio message SID, the AWS SNS message id, or the Infobip bulk id). May be <see langword="null"/> on
    /// success when the provider does not expose an id.
    /// </summary>
    public string? ProviderMessageId { get; private init; }

    /// <summary>
    /// Human-readable failure reason. Non-null whenever <see cref="Success"/> is <see langword="false"/>.
    /// </summary>
    public string? FailureError { get; private init; }

    /// <summary>
    /// Classification of the failure for retry/routing decisions. <see cref="SmsFailureKind.None"/> on success.
    /// </summary>
    public SmsFailureKind FailureKind { get; private init; }

    /// <summary>Creates a response indicating the message was accepted by the provider.</summary>
    /// <param name="providerMessageId">The provider-assigned message id, when available.</param>
    public static SendSingleSmsResponse Succeeded(string? providerMessageId = null)
    {
        return new SendSingleSmsResponse { Success = true, ProviderMessageId = providerMessageId };
    }

    /// <summary>Creates a response indicating the provider rejected the message.</summary>
    /// <param name="failureError">A human-readable failure reason. Must not be <see langword="null"/> or empty.</param>
    /// <param name="failureKind">The failure classification; defaults to <see cref="SmsFailureKind.Unknown"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="failureError"/> is <see langword="null"/> or empty.</exception>
    public static SendSingleSmsResponse Failed(string failureError, SmsFailureKind failureKind = SmsFailureKind.Unknown)
    {
        return new SendSingleSmsResponse
        {
            Success = false,
            FailureError = Argument.IsNotNullOrEmpty(failureError),
            FailureKind = failureKind,
        };
    }

    /// <summary>
    /// Creates a failed response from a caught exception, classifying the failure via
    /// <see cref="SmsFailureKinds.FromException"/>. Used by provider <c>SendAsync</c> implementations to honor
    /// the "return <see cref="Failed(string, SmsFailureKind)"/> rather than throw" contract without risking a
    /// secondary throw when the exception carries an empty <see cref="Exception.Message"/>.
    /// </summary>
    /// <param name="exception">The caught exception. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public static SendSingleSmsResponse FromException(Exception exception)
    {
        Argument.IsNotNull(exception);

        var message = string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;

        return Failed(message, SmsFailureKinds.FromException(exception));
    }
}

/// <summary>Classifies why an SMS send failed, to inform retry and provider-routing decisions.</summary>
[PublicAPI]
public enum SmsFailureKind
{
    /// <summary>The send succeeded; no failure. This is the default for a successful response.</summary>
    None = 0,

    /// <summary>The failure cause is unknown or could not be classified.</summary>
    Unknown,

    /// <summary>A transient transport/network fault (timeout, connection reset). May succeed on retry.</summary>
    Transient,

    /// <summary>The provider rejected the request because of rate limiting.</summary>
    RateLimited,

    /// <summary>The recipient address was invalid, unreachable, or rejected by the provider.</summary>
    InvalidRecipient,

    /// <summary>Authentication or authorization with the provider failed.</summary>
    AuthFailure,

    /// <summary>The provider account has insufficient credit or balance.</summary>
    OutOfCredit,

    /// <summary>
    /// The provider does not support the requested operation (for example a multi-destination batch send on a
    /// provider that only sends to one recipient per call). Retrying or switching the same request to another
    /// instance of the same provider will not help; the caller must change the request or route to a different
    /// provider.
    /// </summary>
    Unsupported,
}
