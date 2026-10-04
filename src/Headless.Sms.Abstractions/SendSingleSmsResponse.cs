// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Sms;

/// <summary>Represents the outcome of sending a single SMS message.</summary>
/// <remarks>
/// A successful send may carry a provider-assigned <see cref="ProviderMessageId"/> when the backend returns
/// one (for example the Twilio message SID, the AWS SNS message identifier, or the Infobip message
/// identifier). A failed send always carries a non-null <see cref="FailureError"/> together with a
/// <see cref="FailureKind"/> that classifies the failure for retry and provider-routing decisions.
/// </remarks>
[PublicAPI]
public sealed class SendSingleSmsResponse
{
    private SendSingleSmsResponse() { }

    /// <summary>
    /// Gets a value indicating whether the provider accepted the message.
    /// </summary>
    [MemberNotNullWhen(false, nameof(FailureError))]
    public bool Success { get; private init; }

    /// <summary>
    /// Gets the provider-assigned identifier for an accepted message when the backend returns one. May be
    /// <see langword="null"/> on success when the provider does not expose an identifier.
    /// </summary>
    public string? ProviderMessageId { get; private init; }

    /// <summary>
    /// Gets the human-readable failure reason. Non-null whenever <see cref="Success"/> is
    /// <see langword="false"/>.
    /// </summary>
    public string? FailureError { get; private init; }

    /// <summary>
    /// Gets the classification of the failure for retry and routing decisions.
    /// <see cref="SmsFailureKind.None"/> on success.
    /// </summary>
    public SmsFailureKind FailureKind { get; private init; }

    /// <summary>Creates a response indicating the provider accepted the message.</summary>
    /// <param name="providerMessageId">The provider-assigned message identifier, when available.</param>
    /// <returns>A new <see cref="SendSingleSmsResponse"/> instance indicating success.</returns>
    public static SendSingleSmsResponse Succeeded(string? providerMessageId = null)
    {
        return new SendSingleSmsResponse { Success = true, ProviderMessageId = providerMessageId };
    }

    /// <summary>Creates a response indicating the provider rejected the message.</summary>
    /// <param name="failureError">The human-readable failure reason.</param>
    /// <param name="failureKind">The failure classification. Defaults to <see cref="SmsFailureKind.Unknown"/>.</param>
    /// <returns>A new <see cref="SendSingleSmsResponse"/> instance indicating failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="failureError"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="failureError"/> is empty.</exception>
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
    /// Creates a failed response from a caught exception with an explicitly classified kind. Failure
    /// classification is the single responsibility of <c>SmsFailureKinds.FromException</c> (in
    /// <c>Headless.Sms</c>), which is Polly-aware, so providers pass its result here rather than have this
    /// contract type re-derive a kind. Surfaces the exception message, falling back to the exception type
    /// name when the message is empty, so the non-empty-message guarantee of
    /// <see cref="Failed(string, SmsFailureKind)"/> always holds.
    /// </summary>
    /// <param name="exception">The caught exception.</param>
    /// <param name="failureKind">The failure classification derived from the provider's own contract.</param>
    /// <returns>A new <see cref="SendSingleSmsResponse"/> instance indicating failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public static SendSingleSmsResponse FromException(Exception exception, SmsFailureKind failureKind)
    {
        Argument.IsNotNull(exception);

        var message = string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;

        return Failed(message, failureKind);
    }
}

/// <summary>Classifies why an SMS send failed, to inform retry and provider-routing decisions.</summary>
/// <remarks>
/// New members may be added in minor versions as providers surface finer-grained failure signals. Consumers
/// that <see langword="switch"/> on this enum must always handle <see cref="Unknown"/> or the
/// <see langword="default"/> case, so a newly added member degrades to treating the failure as unknown
/// rather than falling through unhandled.
/// </remarks>
[PublicAPI]
public enum SmsFailureKind
{
    /// <summary>The send succeeded without error.</summary>
    None = 0,

    /// <summary>The failure cause is unknown or unclassified.</summary>
    Unknown = 1,

    /// <summary>A transient transport or network fault, such as a timeout or connection reset. May succeed on retry.</summary>
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
