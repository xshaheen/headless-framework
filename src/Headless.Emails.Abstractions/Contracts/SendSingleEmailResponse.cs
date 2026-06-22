// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Emails;

/// <summary>
/// The outcome of a single email send attempt.
/// </summary>
/// <remarks>
/// Delivery problems that can be described (transient SMTP errors, non-2xx HTTP responses)
/// are surfaced here rather than thrown, so callers can inspect <see cref="Success"/> without
/// wrapping every send in a try/catch. Unrecoverable faults (for example SMTP authentication
/// failure) are still thrown by the underlying implementation.
/// </remarks>
[PublicAPI]
public sealed class SendSingleEmailResponse
{
    private SendSingleEmailResponse() { }

    /// <summary>
    /// <see langword="true"/> when the provider accepted the message for delivery;
    /// <see langword="false"/> when it was rejected or a non-fatal error occurred.
    /// When <see langword="false"/>, <see cref="FailureError"/> is guaranteed to be non-null.
    /// </summary>
    [MemberNotNullWhen(false, nameof(FailureError))]
    public bool Success { get; private init; }

    /// <summary>
    /// A human-readable description of why the send failed.
    /// <see langword="null"/> when <see cref="Success"/> is <see langword="true"/>.
    /// </summary>
    public string? FailureError { get; private init; }

    /// <summary>Creates a response representing a successful send.</summary>
    public static SendSingleEmailResponse Succeeded()
    {
        return new() { Success = true };
    }

    /// <summary>Creates a response representing a failed send.</summary>
    /// <param name="failureError">A human-readable description of the failure reason.</param>
    public static SendSingleEmailResponse Failed(string failureError)
    {
        return new() { Success = false, FailureError = failureError };
    }
}
