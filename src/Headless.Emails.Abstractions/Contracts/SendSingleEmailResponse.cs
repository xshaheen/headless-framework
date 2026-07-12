// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Emails;

/// <summary>The outcome of a single email send attempt.</summary>
/// <remarks>
/// Delivery problems are reported through this response rather than thrown: every provider-side
/// rejection or transport fault produces a failed response carrying a human-readable
/// <see cref="FailureError"/>. Implementations throw only for cancellation
/// (<see cref="OperationCanceledException"/>) and argument validation (for example a body-less request).
/// A successful send may carry a provider-assigned <see cref="ProviderMessageId"/> when the backend
/// returns one.
/// </remarks>
[PublicAPI]
public sealed class SendSingleEmailResponse
{
    private SendSingleEmailResponse() { }

    /// <summary>
    /// <see langword="true"/> when the provider accepted the message for delivery;
    /// <see langword="false"/> when it was rejected or a delivery/transport error occurred.
    /// When <see langword="false"/>, <see cref="FailureError"/> is guaranteed to be non-null.
    /// </summary>
    [MemberNotNullWhen(false, nameof(FailureError))]
    public bool Success { get; private init; }

    /// <summary>
    /// Provider-assigned identifier for the accepted message when the backend returns one (for example the
    /// SES message id, the Azure Communication Services operation id, or the SMTP server's final response).
    /// May be <see langword="null"/> on success when the provider does not expose an id.
    /// </summary>
    public string? ProviderMessageId { get; private init; }

    /// <summary>
    /// A human-readable description of why the send failed.
    /// Non-null whenever <see cref="Success"/> is <see langword="false"/>.
    /// </summary>
    public string? FailureError { get; private init; }

    /// <summary>Creates a response representing a successful send.</summary>
    /// <param name="providerMessageId">The provider-assigned message id, when available.</param>
    public static SendSingleEmailResponse Succeeded(string? providerMessageId = null)
    {
        return new() { Success = true, ProviderMessageId = providerMessageId };
    }

    /// <summary>Creates a response representing a failed send.</summary>
    /// <param name="failureError">A human-readable failure reason. Must not be <see langword="null"/> or empty.</param>
    /// <exception cref="ArgumentException"><paramref name="failureError"/> is <see langword="null"/> or empty.</exception>
    public static SendSingleEmailResponse Failed(string failureError)
    {
        return new() { Success = false, FailureError = Argument.IsNotNullOrEmpty(failureError) };
    }

    /// <summary>
    /// Creates a failed response from a caught exception, surfacing the provider's raw error message
    /// (falling back to the exception type name when the message is empty) so the non-empty-message
    /// guarantee of <see cref="Failed(string)"/> always holds.
    /// </summary>
    /// <param name="exception">The caught exception. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public static SendSingleEmailResponse FromException(Exception exception)
    {
        Argument.IsNotNull(exception);

        var message = string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;

        return Failed(message);
    }
}
