// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Emails;

/// <summary>Represents the outcome of an email delivery attempt.</summary>
/// <remarks>
/// Delivery problems are reported through this response rather than thrown: every provider-side rejection or
/// transport fault produces a failed response carrying a human-readable <see cref="FailureError"/>. A failed
/// response always carries a non-null <see cref="FailureError"/>. Implementations throw only for cancellation
/// and argument validation. A successful send may carry a provider-assigned <see cref="ProviderMessageId"/>
/// when the backend returns one, such as the SES message identifier, the Azure Communication Services
/// operation identifier, or the SMTP server's final response.
/// </remarks>
[PublicAPI]
public sealed class SendSingleEmailResponse
{
    private SendSingleEmailResponse() { }

    /// <summary>
    /// Gets a value indicating whether the provider accepted the message for delivery. When
    /// <see langword="false"/>, <see cref="FailureError"/> is guaranteed to be non-null.
    /// </summary>
    [MemberNotNullWhen(false, nameof(FailureError))]
    public bool Success { get; private init; }

    /// <summary>
    /// Gets the provider-assigned identifier for an accepted message when the backend returns one. May be
    /// <see langword="null"/> on success when the provider does not expose an identifier.
    /// </summary>
    public string? ProviderMessageId { get; private init; }

    /// <summary>
    /// Gets a human-readable description of why the send failed. Non-null whenever <see cref="Success"/>
    /// is <see langword="false"/>.
    /// </summary>
    public string? FailureError { get; private init; }

    /// <summary>Creates a response representing a successful delivery attempt.</summary>
    /// <param name="providerMessageId">The provider-assigned message identifier, when available.</param>
    /// <returns>A successful <see cref="SendSingleEmailResponse"/> instance.</returns>
    public static SendSingleEmailResponse Succeeded(string? providerMessageId = null)
    {
        return new() { Success = true, ProviderMessageId = providerMessageId };
    }

    /// <summary>Creates a response representing a failed delivery attempt.</summary>
    /// <param name="failureError">The human-readable failure reason.</param>
    /// <returns>A failed <see cref="SendSingleEmailResponse"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="failureError"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="failureError"/> is empty.</exception>
    public static SendSingleEmailResponse Failed(string failureError)
    {
        return new() { Success = false, FailureError = Argument.IsNotNullOrEmpty(failureError) };
    }

    /// <summary>
    /// Creates a failed response from a caught exception, surfacing the exception's message and falling back
    /// to the exception type name when the message is empty, so the non-empty-message guarantee of
    /// <see cref="Failed(string)"/> always holds.
    /// </summary>
    /// <param name="exception">The caught exception.</param>
    /// <returns>A failed <see cref="SendSingleEmailResponse"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public static SendSingleEmailResponse FromException(Exception exception)
    {
        Argument.IsNotNull(exception);

        var message = string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;

        return Failed(message);
    }
}
