// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Sms;

/// <summary>Represents the outcome of sending a single SMS message.</summary>
/// <remarks>
/// A successful send can include a provider-assigned <see cref="ProviderMessageId"/> when returned by the backend.
/// A failed send includes <see cref="FailureError"/> and a <see cref="FailureKind"/> that classifies the failure
/// for retry and routing decisions.
/// </remarks>
[PublicAPI]
public sealed class SendSingleSmsResponse
{
    private SendSingleSmsResponse() { }

    /// <summary>Gets a value indicating whether the provider accepted the message.</summary>
    [MemberNotNullWhen(false, nameof(FailureError))]
    public bool Success { get; private init; }

    /// <summary>
    /// Gets the provider-assigned identifier for an accepted message, such as a Twilio message SID, AWS SNS message
    /// identifier, or Infobip message identifier.
    /// </summary>
    public string? ProviderMessageId { get; private init; }

    /// <summary>
    /// Gets the failure reason when <see cref="Success"/> is <see langword="false"/>.
    /// </summary>
    public string? FailureError { get; private init; }

    /// <summary>
    /// Gets the failure classification for retry and routing decisions. Returns <see cref="SmsFailureKind.None"/> on success.
    /// </summary>
    public SmsFailureKind FailureKind { get; private init; }

    /// <summary>Creates a successful response indicating provider acceptance.</summary>
    /// <param name="providerMessageId">The provider-assigned message identifier, when available.</param>
    /// <returns>A new <see cref="SendSingleSmsResponse"/> instance indicating success.</returns>
    public static SendSingleSmsResponse Succeeded(string? providerMessageId = null)
    {
        return new SendSingleSmsResponse { Success = true, ProviderMessageId = providerMessageId };
    }

    /// <summary>Creates a response indicating provider rejection.</summary>
    /// <param name="failureError">The failure reason.</param>
    /// <param name="failureKind">The failure classification.</param>
    /// <returns>A new <see cref="SendSingleSmsResponse"/> instance indicating failure.</returns>
    /// <exception cref="ArgumentException"><paramref name="failureError"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="failureError"/> is <see langword="null"/>.</exception>
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
    /// Creates a failed response from an exception with an explicit failure classification.
    /// </summary>
    /// <param name="exception">The caught exception.</param>
    /// <param name="failureKind">The failure classification derived by the caller.</param>
    /// <returns>A new <see cref="SendSingleSmsResponse"/> instance indicating failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public static SendSingleSmsResponse FromException(Exception exception, SmsFailureKind failureKind)
    {
        Argument.IsNotNull(exception);

        var message = string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;

        return Failed(message, failureKind);
    }
}

/// <summary>Specifies categories of SMS transmission failures to guide retry and provider routing decisions.</summary>
/// <remarks>
/// Callers switching on this enum must handle the default case to accommodate future additions.
/// </remarks>
[PublicAPI]
public enum SmsFailureKind
{
    /// <summary>The send succeeded without error.</summary>
    None = 0,

    /// <summary>The failure cause is unknown or unclassified.</summary>
    Unknown = 1,

    /// <summary>A transient transport or network fault occurred, such as a timeout or connection reset.</summary>
    Transient = 2,

    /// <summary>The provider rejected the request due to rate limiting.</summary>
    RateLimited = 3,

    /// <summary>The recipient address is invalid, unreachable, or rejected by the provider.</summary>
    InvalidRecipient = 4,

    /// <summary>Authentication or authorization with the provider failed.</summary>
    AuthFailure = 5,

    /// <summary>The provider account has insufficient credit or balance.</summary>
    OutOfCredit = 6,
}
