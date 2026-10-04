// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Emails;

/// <summary>Represents the outcome of an email delivery attempt.</summary>
/// <remarks>
/// Delivery failures and transport errors return a response object where <see cref="Success"/> is
/// <see langword="false"/> and <see cref="FailureError"/> contains details. Methods throw only for
/// cancellation or invalid arguments.
/// </remarks>
[PublicAPI]
public sealed class SendSingleEmailResponse
{
    private SendSingleEmailResponse() { }

    /// <summary>
    /// Gets a value indicating whether the provider accepted the message for delivery.
    /// </summary>
    [MemberNotNullWhen(false, nameof(FailureError))]
    public bool Success { get; private init; }

    /// <summary>
    /// Gets the provider-assigned identifier for an accepted message when available.
    /// </summary>
    public string? ProviderMessageId { get; private init; }

    /// <summary>
    /// Gets a description of why the send attempt failed.
    /// </summary>
    public string? FailureError { get; private init; }

    /// <summary>Creates a response representing a successful delivery attempt.</summary>
    /// <param name="providerMessageId">The provider-assigned message identifier.</param>
    /// <returns>A successful <see cref="SendSingleEmailResponse"/> instance.</returns>
    public static SendSingleEmailResponse Succeeded(string? providerMessageId = null)
    {
        return new() { Success = true, ProviderMessageId = providerMessageId };
    }

    /// <summary>Creates a response representing a failed delivery attempt.</summary>
    /// <param name="failureError">The failure reason description.</param>
    /// <returns>A failed <see cref="SendSingleEmailResponse"/> instance.</returns>
    /// <exception cref="ArgumentException"><paramref name="failureError"/> is <see langword="null"/> or empty.</exception>
    public static SendSingleEmailResponse Failed(string failureError)
    {
        return new() { Success = false, FailureError = Argument.IsNotNullOrEmpty(failureError) };
    }

    /// <summary>
    /// Creates a failed response from a caught exception.
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
